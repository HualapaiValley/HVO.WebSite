# HVO.Staging

Active .NET 10 library bridging staged `HVO.Astronomy` and `HVO.Weather` APIs
until promotion to the SDK packages. It depends on those two packages and has no
process startup/configuration. [Astronomy](Astronomy/) contains moon rise/set,
sun/moon extensions and CelestialArcCalculations; [Weather](Weather/) contains DewRisk.

This is production-used code: the Davis
[DavisHomeAssistantProjection](../HVO.Hardware.DavisVantagePro2/HomeAssistant/DavisHomeAssistantProjection.cs)
calls `CelestialArcCalculations.BuildMoonSnapshot` for phase, illumination and
rise/set projection. Unit tests also consume the bridge. Retiring it requires
[#372](https://github.com/HualapaiValley/HVO.WebSite/issues/372) and
[SDK #82](https://github.com/RoySalisbury/HVO.SDK/issues/82) plus consumer/package
reconciliation. The SDK issue's older test-only sentence is not current authority.

Use the exact root SDK and locked restore/build. Focused validation:

```bash
dotnet test tests/HVO.Hardware.DavisVantagePro2.Tests --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
dotnet test tests/HVO.WebSite.UnitTests --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

No SDK repository edit or migration is implied. [Architecture](../../docs/ARCHITECTURE.md)
and [test owners](../../tests/README.md) describe this compatibility boundary.
