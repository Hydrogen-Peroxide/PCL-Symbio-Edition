using BedrockDataRegression;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Bedrock data regressions require Windows and temporary NTFS junctions.");
    return 1;
}

var selectedSuite = args switch
{
    [] => "all",
    ["--suite", "data"] => "data",
    ["--suite", "registration"] => "registration",
    _ => "invalid"
};
if (selectedSuite == "invalid")
{
    Console.Error.WriteLine("Usage: BedrockDataRegression [--suite data|registration]");
    return 2;
}

using var suite = new RegressionSuite();
if (selectedSuite is "all" or "data")
{
    FileSystemTests.Run(suite);
    IsolationTests.Run(suite);
    LaunchLeaseTests.Run(suite);
    InstallTransactionTests.Run(suite);
}
if (selectedSuite is "all" or "registration") PackageRegistrationTests.Run(suite);
return suite.Finish();
