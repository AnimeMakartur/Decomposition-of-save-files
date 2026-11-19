using Colossal.Logging;
using Game;
using Game.Modding;

namespace Cs2DataExporter
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger("Cs2DataExporter").SetShowsErrorsInUI(true);

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info("Завантаження Cs2DataExporter...");
            // Реєструємо нашу систему в ігровому циклі
            updateSystem.UpdateAt<CityDataExportSystem>(SystemUpdatePhase.MainLoop);
        }

        public void OnDispose()
        {
            log.Info("Вивантаження Cs2DataExporter.");
        }
    }
}