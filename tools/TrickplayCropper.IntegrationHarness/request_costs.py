"""Read-only, fixed-distribution localhost measurements; deploy/restart separately.

Run immediately after deployment, before any Preview request, to observe first loads.
Only redacted samples are retained. Does not create credentials or mutate media/policy.
"""
import argparse
import concurrent.futures
import hashlib
import http.client
import json
import pathlib
import statistics
import time
import uuid
from collections import defaultdict

ROOT = pathlib.Path(__file__).resolve().parents[2]


def render(context, samples):
    groups = defaultdict(list)
    for sample in samples:
        label = f'{sample["phase"]} {sample["method"]} {sample["status"]} {sample["cache"]}'.strip()
        groups[label].append(sample["milliseconds"])
    lines = ["# Request cost report", "", f'- Label: **{context["label"]}**',
             f'- Total requests: **{len(samples)}**', f'- Elapsed: **{context["elapsed_seconds"]:.3f} s**', "",
             "| Category | Count | Minimum (ms) | Maximum (ms) | Median (ms) | Mean (ms) |",
             "| --- | ---: | ---: | ---: | ---: | ---: |"]
    for label, values in sorted(groups.items()):
        lines.append(f"| {label} | {len(values)} | {min(values):.3f} | {max(values):.3f} | "
                     f"{statistics.median(values):.3f} | {statistics.mean(values):.3f} |")
    lines += ["", "## Context", "", "```json", json.dumps(context, indent=2), "```", "",
              "Counts cover measured Preview requests only; metadata/readiness traffic is excluded. Durations cover dispatch through buffered response body, excluding assertions. Each lane reuses one HTTP/1.1 connection.",
              "First-load samples include JIT and plugin cold observations; OS caches are not flushed. Warm labels describe request order, not measured backing I/O.",
              "A nonmember-source GET 404 still performs current authorization/source checks; it is not evidence of GET metadata-negative reuse.",
              "No latency budget is asserted. Fake-clock expiry/regeneration and backing operation counts belong to component tests.", ""]
    return "\n".join(lines)


class Client:
    def __init__(self, token):
        self.connection = http.client.HTTPConnection("localhost", 8096, timeout=10)
        self.authorization = f'MediaBrowser Client="TrickplayHarness", Device="local", DeviceId="trickplay-harness", Version="1.0", Token="{token}"'

    def close(self):
        self.connection.close()

    def request(self, method, route, headers=None):
        supplied = {"Authorization": self.authorization}
        supplied.update(headers or {})
        started = time.perf_counter()
        self.connection.request(method, route, headers=supplied)
        response = self.connection.getresponse()
        body = response.read()
        elapsed = (time.perf_counter() - started) * 1000
        return response.status, dict((k.lower(), v) for k, v in response.getheaders()), body, elapsed

    def read_json(self, route):
        status, _, body, _ = self.request("GET", route)
        if status != 200:
            raise ValueError("Host metadata request failed")
        return json.loads(body)


def wait_for_host(client):
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        try:
            status, _, body, _ = client.request("GET", "/System/Info/Public")
            if status == 200:
                return json.loads(body)["Version"]
            if status != 503:
                raise ValueError("Host readiness request rejected")
        except (OSError, http.client.HTTPException):
            client.close()
        time.sleep(1)
    raise ValueError("Host readiness deadline exceeded")


def measure(client, phase, method, route, samples, expected, headers=None):
    started = time.perf_counter()
    try:
        status, received, body, duration = client.request(method, route, headers)
    except Exception:
        samples.append(dict(phase=phase, method=method, status="transport-failure", cache="",
                            milliseconds=(time.perf_counter() - started) * 1000))
        raise ValueError("Measured transport request failed") from None
    samples.append(dict(phase=phase, method=method, status=status,
                        cache=received.get("x-trickplay-cache", ""), milliseconds=duration,
                        server_timing=received.get("server-timing", "")))
    if status != expected:
        raise ValueError("Measured response status disagrees with the fixture contract")
    if method == "HEAD" and (body or "etag" in received or "server-timing" in received):
        raise ValueError("HEAD exposed a representation")
    if status in (200, 304) and "x-trickplay-frame-index" not in received:
        raise ValueError("Successful Preview omitted its current Frame Index")
    return received


def route(item, ticks=0, source=None):
    result = f"/TrickplayCropper/Videos/{item}/Preview?PositionTicks={ticks}"
    return result + (f"&MediaSourceId={source}" if source else "")


def subjects(client, inputs):
    user = client.read_json("/Users/Me")
    configuration = client.read_json("/System/Configuration")["TrickplayOptions"]
    result = []
    for item in inputs["playableItemIds"]:
        sources = client.read_json(f'/Items/{item}/PlaybackInfo?userId={user["Id"]}')["MediaSources"]
        source = next(s for s in sources if uuid.UUID(s["Id"]) == uuid.UUID(item))
        video = next(s for s in source["MediaStreams"] if s["Type"] == "Video")
        target = min(configuration["WidthResolutions"])
        width = min(target, video.get("Width") or target) // 2 * 2
        rows = client.read_json(f'/Items?ids={item}&userId={user["Id"]}&fields=Trickplay&enableImages=false')["Items"][0]["Trickplay"]
        rows = next(rows[k] for k in rows if uuid.UUID(k) == uuid.UUID(item))
        metadata = rows[str(width)]
        count, interval = metadata["ThumbnailCount"], metadata["Interval"]
        if count < 5 or interval <= 0 or metadata["Width"] != width:
            raise ValueError("Fixture lacks usable exact-width generated metadata")
        result.append(dict(item=item, width=width, count=count, interval=interval,
                           sources=[s["Id"] for s in sources]))
    return result


def workload(client, token, fixtures, samples):
    for fixture in fixtures:
        item = fixture["item"]
        measure(client, "first-load", "HEAD", route(item), samples, 200)
        measure(client, "first-jpeg", "GET", route(item), samples, 200)
        # The explicit default ID is an independent source-facts key but resolves the same Source Video.
        measure(client, "explicit-default", "HEAD", route(item, source=item), samples, 200)
    for method in ("HEAD", "GET"):
        for index in range(120):
            fixture = fixtures[index % 2]
            measure(client, "serial-warm", method, route(fixture["item"]), samples, 200)
    for fixture in fixtures:
        item = fixture["item"]
        for frame in range(1, 5):
            path = route(item, frame * fixture["interval"] * 10000)
            head = measure(client, "new-frame", "HEAD", path, samples, 200)
            current = measure(client, "new-frame", "GET", path, samples, 200)
            if head["x-trickplay-frame-index"] != str(frame) or current["x-trickplay-frame-index"] != str(frame):
                raise ValueError("Stable generated snapshot returned an incorrect Frame Index")
            conditional = measure(client, "conditional", "GET", path, samples, 304,
                                  {"If-None-Match": current["etag"]})
            if conditional["x-trickplay-frame-index"] != str(frame):
                raise ValueError("Conditional GET returned an incorrect Frame Index")
    for fixture in fixtures:
        absent = "55555555555555555555555555555555"
        if any(uuid.UUID(s) == uuid.UUID(absent) for s in fixture["sources"]):
            raise ValueError("Negative source unexpectedly belongs to a fixture")
        for index in range(30):
            for method in ("HEAD", "GET"):
                measure(client, "negative-source-first" if index == 0 else "negative-source-repeat", method,
                        route(fixture["item"], source=absent), samples, 404)
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
        # Each worker owns its connection and returns partial samples even on failure.
        futures = [executor.submit(lane, token, fixtures, index) for index in range(6)]
        failures = []
        for future in futures:
            observations, failure = future.result()
            samples.extend(observations)
            failures.append(failure)
        if any(failures):
            raise ValueError("A six-lane request failed")


def lane(token, fixtures, ordinal):
    client, samples, failed = Client(token), [], False
    try:
        client.read_json("/System/Info/Public")
        for index in range(60):
            fixture = fixtures[(index + ordinal) % 2]
            method = "HEAD" if index % 2 == 0 else "GET"
            measure(client, "six-lane-warm", method, route(fixture["item"]), samples, 200)
    except Exception:
        failed = True
    finally:
        client.close()
    return samples, failed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--label", required=True, choices=["before", "after"])
    arguments = parser.parse_args()
    inputs = json.loads((ROOT / "harness.json").read_text())
    client = Client(inputs["adminToken"])
    samples = []
    context = dict(label=arguments.label, build="Debug", logging="plugin Debug; host configuration unchanged",
                   credential="same supplied administrator user token", connection="HTTP/1.1 persistent per lane",
                   elapsed_seconds=0, outcome="failed", distribution="2 Items; 120 serial requests/method; 6 lanes x 60 alternating requests")
    started = time.perf_counter()
    try:
        context["jellyfin"] = wait_for_host(client)
        binaries = list(pathlib.Path("/var/lib/jellyfin/plugins").glob("*/Jellyfin.Plugin.TrickplayCropper.dll"))
        if len(binaries) != 1:
            raise ValueError("Cannot identify the single deployed plugin binary")
        plugins = client.read_json("/Plugins")
        active = [plugin for plugin in plugins if plugin["Id"].replace("-", "").lower() == "630fb7589a294f2ca54c95793651bb8a"]
        if len(active) != 1 or active[0]["Status"] != "Active":
            raise ValueError("The deployed plugin is not active")
        context["plugin_version"] = active[0]["Version"]
        context["deployed_dll_sha256"] = hashlib.sha256(binaries[0].read_bytes()).hexdigest()
        fixtures = subjects(client, inputs)
        context["fixtures"] = [dict(ordinal=i + 1, width=f["width"], interval_ms=f["interval"],
                                    count=f["count"], enumerated_sources=len(f["sources"])) for i, f in enumerate(fixtures)]
        started = time.perf_counter()
        workload(client, inputs["adminToken"], fixtures, samples)
        context["outcome"] = "passed"
    except Exception:
        print("Request measurement failed; redacted partial report retained.")
    finally:
        client.close()
        context["elapsed_seconds"] = time.perf_counter() - started
        directory = ROOT / "test-output"
        directory.mkdir(exist_ok=True)
        name = f'request-costs-{arguments.label}-{time.time_ns()}'
        (directory / (name + ".json")).write_text(json.dumps(dict(context=context, samples=samples), indent=2) + "\n")
        (directory / (name + ".md")).write_text(render(context, samples))
        print(render(context, samples))
    return 0 if context["outcome"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
