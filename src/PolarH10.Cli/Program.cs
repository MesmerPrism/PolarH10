using System.CommandLine;
using PolarH10.Cli.Commands;

var root = new RootCommand("Polar H10 direct BLE reference tool")
{
    ScanCommand.Create(),
    MonitorCommand.Create(),
    RecordCommand.Create(),
    StreamCommand.Create(),
    DoctorCommand.Create(),
    ReplayCommand.Create(),
    SessionsCommand.Create(),
    ProtocolCommand.Create(),
};

return await root.InvokeAsync(args);
