using Colossal.Logging;
using Game;
using Game.Modding;

namespace Cs2DataExporter
{
    public class Mod : IMod
    {
        // Створюємо логер для виведення інформації
        public static ILog log = LogManager.GetLogger("Cs2DataExporter").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info("Мод завантажено: ініціалізація експортера даних...");
            // Далі тут ми будемо реєструвати систему для читання ECS
        }

        public void OnDispose()
        {
            log.Info("Мод вимкнено.");
        }
    }
}