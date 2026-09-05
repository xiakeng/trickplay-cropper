using TrickplayCropper.IntegrationHarness;

try
{
    return await new HarnessApplication().RunAsync(args).ConfigureAwait(false);
}
catch (Exception)
{
    // Credentials can appear in JSON/HTTP exception details. Emit only a fixed diagnostic.
    Console.Error.WriteLine("Harness aborted. Check the input shape, subject roles, local service, and required tools. No credentials emitted.");
    return 1;
}
