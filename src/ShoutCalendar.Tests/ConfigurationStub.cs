// Only the configuration interface is needed to exercise the real plugin save converter without loading the game.
namespace Dalamud.Configuration;
public interface IPluginConfiguration { int Version { get; set; } }
