using System;

namespace XRay.Importer.Levels
{
    [Flags]
    public enum XRayWeatherLevelOverrides
    {
        None = 0,
        TimeScale = 1 << 0,
        TimeOfDay = 1 << 1,
        WeatherProfile = 1 << 2,
        WeatherCatalog = 1 << 3,
        WeatherFxCatalog = 1 << 4,
        Fog = 1 << 5,
        SunShafts = 1 << 6,
        LensFlare = 1 << 7,
        ExpressiveDaylight = 1 << 8,
        LensFlareIntensity = 1 << 9,
        FlareAdaptation = 1 << 10,
        ExpressiveFlareIntensity = 1 << 11,
        FxLightingAdaptation = 1 << 12,
        CelestialSettings = 1 << 13,
        Precipitation = 1 << 14
    }
}
