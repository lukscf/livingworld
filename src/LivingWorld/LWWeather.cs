using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWWeather — Etapa 3 (plano "mundo mais vivo"): clima com efeito real,
    // nao so flavor text. Ate aqui WeatherLines (LWDialogue) era 100%
    // aleatorio por fala, sem estado nenhum. Agora existe um clima ATUAL
    // compartilhado que empurra comportamento (TickNeeds) e escolhe a fala
    // certa pro clima certo. Nao persiste entre boots (reseta em Clear,
    // roda naturalmente) -- estado cosmetico/comportamental, nao lore.
    // ===================================================================
    public enum LWWeatherKind { Clear, Rain, Storm, Cold }

    public static class LWWeather
    {
        public static LWWeatherKind Current { get; private set; } = LWWeatherKind.Clear;

        public static void Tick()
        {
            double roll = Utility.RandomDouble();
            if (roll < 0.55) Current = LWWeatherKind.Clear;
            else if (roll < 0.75) Current = LWWeatherKind.Rain;
            else if (roll < 0.88) Current = LWWeatherKind.Cold;
            else Current = LWWeatherKind.Storm;
        }
    }
}
