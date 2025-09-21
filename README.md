```
git submodule add https://github.com/oscar-wos/RLogger
dotnet sln .\Plugin.sln add .\RLogger\src\RLogger.csproj
dotnet add .\Plugin.csproj reference .\RLogger\src\RLogger.csproj
```

```csharp
using RLogger;

private Logger? _logger;

public override void Load(bool hotReload)
{
    string logPath = Path.Join(Server.GameDirectory, "csgo", "logs", "Plugin");
    _logger = new(logPath, Logger);
}

private void Log()
{
    _logger.Debug();
    _logger.Information();
    _logger.Warning();
    _logger.Error();
}

private void ExceptionLog()
{
    try { }
    catch (Exception ex)
    {
        throw _logger.Critical("Plugin.ExceptionLog()", ex);
    }
}
```