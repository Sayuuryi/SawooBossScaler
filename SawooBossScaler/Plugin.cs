using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;

namespace SawooBossScaler;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/sbs";
    private const string CommandAlias = "/bossscaler";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IPluginLog _log;
    private readonly WindowSystem _windowSystem = new("SawooBossScaler");
    private readonly ActionWatcher? _actions;
    private readonly BossScaler? _scaler;
    private readonly MainWindow? _mainWindow;
    private bool _hooked;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        ICondition condition,
        IDataManager dataManager,
        IGameInteropProvider gameInteropProvider,
        ITargetManager targetManager)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _log = log;

        try
        {
            var config = pluginInterface.GetPluginConfig() as Config ?? new Config();
            var names = new BossNames(dataManager, log);
            var penumbra = new PenumbraRedraw(pluginInterface);
            _actions = ActionWatcher.TryCreate(gameInteropProvider, log);
            _scaler = new BossScaler(framework, clientState, objectTable, condition, targetManager, log, config, names, _actions, penumbra);

            _mainWindow = new MainWindow(pluginInterface, config, _scaler, penumbra);
            _windowSystem.AddWindow(_mainWindow);

            _commandManager.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open Sawoo Boss Scaler. Add \"config\" to open the settings." });
            _commandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand) { ShowInHelp = false });

            _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
            _pluginInterface.UiBuilder.OpenMainUi += ToggleMain;
            _pluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
            _hooked = true;
        }
        catch (Exception ex)
        {
            // undo whatever was set up, so a failed start never leaves hooks or listeners behind
            _log.Error(ex, "Sawoo Boss Scaler failed to start");
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_hooked)
        {
            _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
            _pluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
            _pluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
            _hooked = false;
        }

        Try(() => _commandManager.RemoveHandler(Command));
        Try(() => _commandManager.RemoveHandler(CommandAlias));

        // the scaler puts every boss back before the hook goes away
        Try(() => _scaler?.Dispose());
        Try(() => _actions?.Dispose());
        Try(_windowSystem.RemoveAllWindows);
    }

    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error while shutting down");
        }
    }

    private void ToggleMain() => _mainWindow?.Toggle();

    private void OpenSettings() => _mainWindow?.Open(MainWindow.Tab.Settings);

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
            OpenSettings();
        else
            ToggleMain();
    }
}
