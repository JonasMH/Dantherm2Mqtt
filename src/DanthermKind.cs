
using NodaTime;
using System.Net;


public class DanthermKind
{
    public string Kind { get; set; } = "DanthermUvc";
    public DanthermUvcSpec Spec { get; set; } = new DanthermUvcSpec();
    public DanthermUvcStatus Status { get; set; } = new DanthermUvcStatus();
}

public class DanthermUvcSpec
{
    public string Address { get; set; }
    public int Port { get; set; } = 502;
    public byte SlaveAddress { get; set; } = 1;
    public int PollingIntervalMS { get; set; } = 30_000;
}

public class DanthermUvcStatus
{
    public string MacAddress { get; set; }
    public ulong SerialNum { get; set; }
    public string SystemName { get; set; }
    public DanthermUvcFwVersion FWVersion { get; set; }
    public DanthermUvcSystemId SystemId { get; set; }
    public bool HalLeft { get; set; }
    public bool HalRight { get; set; }
    public Instant DateTime { get; set; }
    public long WorkTimeHours { get; set; }
    public Instant StartExploitation { get; set; }
    public DanthermUvcModeOfOperation CurrentBLState { get; set; }
    public float OutdoorTemperatureC { get; set; }
    public float SupplyTemperatureC { get; set; }
    public float ExtractTemperatureC { get; set; }
    public float ExhaustTemperatureC { get; set; }
    public uint FilterRemaningTimeDays { get; set; }
    public DanthermUvcAlarm LastActiveAlarm { get; set; }
    public float HALFan1Rpm { get; set; }
    public float HALFan2Rpm { get; set; }
    public uint? VolatileOrganicCompounds { get; set; }
    public uint? RelativeHumidity { get; set; }
    public DanthermUvcBypassState? BypassState { get; set; }
}

public enum DanthermUvcBypassState
{
    Closed = 0x0000,
    InProcess = 0x0001,
    Closing = 0x0020,
    Opening = 0x0040,
    Opened = 0x00FF
}

public enum DanthermUvcAlarm
{
    None = 0,
    ExhaustFAN = 1,
    SupplyFAN = 2,
    Bypass = 3,
    T1 = 4,
    T2 = 5,
    T3 = 6,
    T4 = 7,
    T5 = 8,
    RH = 9,
    Outdoor13 = 10,
    Supply5 = 11,
    Fire = 12,
    Communication = 13,
    FireTermostat = 14,
    HighWaterLevel = 15
}

public enum DanthermUvcModeOfOperation
{
    Standby = 0,
    Manual = 1,
    Demand = 2,
    WeekProgram = 3,
    Away = 5,
    Summer = 6,
    DIOverride = 7,
    Hygrostat = 8,
    FireplaceBoost = 9,
    InstallerMode = 10,
    FailSafe1 = 11,
    FailSafe2 = 12,
    FailOff = 13,
    DefrostOff = 14,
    Defrost = 15,
    NightMode = 16
}

public enum DanthermUvcSetModeOfOperation
{
    Demand = 0x0002,
    Manual = 0x0004,
    WeekProgram = 0x0008,

    StartAway = 0x0010,
    EndAway = 0x8010,

    StartFireplace = 0x0040,
    EndFireplace = 0x8040,

    StartSummer = 0x0800,
    EndSummer = 0x8800,
}


public class DanthermUvcFwVersion
{
    public byte Major { get; set; }
    public byte Minor { get; set; }

    public static DanthermUvcFwVersion Parse(byte[] input)
    {
        return new DanthermUvcFwVersion()
        {
            Major = input[1],
            Minor = input[0]
        };
    }
}

public class DanthermUvcSystemId
{
    public bool FP1 { get; set; }
    public bool Week { get; set; }
    public bool Bypass { get; set; }
    public bool LRSwitch { get; set; }
    public bool InternalPreheater { get; set; }
    public bool RHSensor { get; set; }
    public bool VOCSensor { get; set; }
    public bool ExtOverride { get; set; }
    public bool HAC1 { get; set; }
    public bool HRC2 { get; set; }
    public bool PCTool { get; set; }
    public bool Apps { get; set; }
    public bool ZigBee { get; set; }
    public bool DI1Override { get; set; }
    public bool DI2Override { get; set; }

    public DanthermUvcUnitType UnitType { get; set; }

    public static DanthermUvcSystemId Parse(byte[] input)
    {
        var result = new DanthermUvcSystemId();
        var components = (input[0] << 8) + input[1];

        result.FP1 = ((components >> 0) & 0x01) == 1;
        result.Week = ((components >> 1) & 0x01) == 1;
        result.Bypass = ((components >> 2) & 0x01) == 1;
        result.LRSwitch = ((components >> 3) & 0x01) == 1;
        result.InternalPreheater = ((components >> 4) & 0x01) == 1;
        result.RHSensor = ((components >> 5) & 0x01) == 1;
        result.VOCSensor = ((components >> 6) & 0x01) == 1;
        result.ExtOverride = ((components >> 7) & 0x01) == 1;
        result.HAC1 = ((components >> 8) & 0x01) == 1;
        result.HRC2 = ((components >> 9) & 0x01) == 1;
        result.PCTool = ((components >> 10) & 0x01) == 1;
        result.Apps = ((components >> 11) & 0x01) == 1;
        result.ZigBee = ((components >> 12) & 0x01) == 1;
        result.DI1Override = ((components >> 13) & 0x01) == 1;
        result.DI2Override = ((components >> 14) & 0x01) == 1;

        result.UnitType = (DanthermUvcUnitType)input[3];

        return result;
    }
}

public enum DanthermUvcUnitType
{
    WG200 = 1,
    WG300 = 2,
    WG500 = 3,
    HCC2 = 4,
    HCC2_ALU = 5,
    HCV300_ALU = 6,
    HCV500_ALU = 7,
    HCV700_ALU = 8,
    HCV400_P2 = 9,
    HCV400_E1 = 10,
    HCV400_P1 = 11,
    HCC2_E1 = 12,
}