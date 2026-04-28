namespace HVO.Hardware.DavisVantagePro2.Station;

/// <summary>DST mode as stored in the Davis console EEPROM.</summary>
public enum DstMode { Auto, On, Off }

/// <summary>Console temperature logging mode.</summary>
public enum TempLogging { Last, Average }

/// <summary>Davis transmitter types for the 8 ISS channels.</summary>
public enum TransmitterType
{
    Iss          = 0,
    TempOnly     = 1,
    HumidityOnly = 2,
    TempHumidity = 3,
    Wind         = 4,
    Rain         = 5,
    Leaf         = 6,
    Soil         = 7,
    LeafSoil     = 8,
    SensorLink   = 9,
    None         = 10
}
