module Configuration

open Domain

let byDefault = {
    Cycles = 10
    Resources = [
        CpuResource {CpuResourceName = "DC1"; Cpu = 0}
        LimitedResource {LimitedResourceName = "DBX"; MaxRatePerCycle = 10}
    ]
    Clients = ["BE1"; "BE2"; "BE3"]
    Events = Map [
        1, [{Client = "BE1"; Resource = "DC1"; RequestsIncrement = 200}]
        2, [{Client = "BE1"; Resource = "DBX"; RequestsIncrement = 200}]
        3, [{Client = "BE2"; Resource = "DC1"; RequestsIncrement = 1000}]
        4, [{Client = "BE2"; Resource = "DBX"; RequestsIncrement = 1000}]
        5, [{Client = "BE3"; Resource = "DC1"; RequestsIncrement = 50}]
        6, [{Client = "BE3"; Resource = "DBX"; RequestsIncrement = 50}]
    ]
}

let custom = {
    Cycles = 10
    Resources = [
        //CpuResource {CpuResourceName = "DC1"; Cpu = 50}
        LimitedResource {LimitedResourceName = "DC1"; MaxRatePerCycle = 10}
    ]
    Clients = ["BE1"; "BE2"; "BE3"]
    Events = Map [
        2, [
            {Client = "BE1"; Resource = "DC1"; RequestsIncrement = 70}
            {Client = "BE2"; Resource = "DC1"; RequestsIncrement = 50}
        ]
        5, [{Client = "BE3"; Resource = "DC1"; RequestsIncrement = 30}]
    ]
}
