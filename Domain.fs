namespace Domain

type LimitedResource = {
    LimitedResourceName : string
    MaxRatePerCycle : int
}

type CpuResource = {
    CpuResourceName : string
    Cpu : int
}

type RatedResource =
    | LimitedResource of LimitedResource
    | CpuResource of CpuResource

type ResourceName = string
type ClientName = string

type ClientResourceData = {
    PendingRequests : int
    Apportion : float
    TargetResource : RatedResource
}

type Client = {
    Name : string
    Resources : Map<ResourceName, ClientResourceData>
}

type GaugerPendingClientData = {
    PendingRequests : int
    LastActivityCycle : int
}

type Gauger = {
    CurrentCycle : int
    GlobalPendingClientData : Map<ClientName * ResourceName, GaugerPendingClientData>
    GlobalPendingTotals : Map<ResourceName, int>
}

type IncrementRequestsEvent = {
    Client : string
    Resource : string
    RequestsIncrement : int
}

type Configuration = {
    Cycles : int
    Resources : RatedResource list
    Clients : string list
    Events : Map<int, IncrementRequestsEvent list>
}

type ToGaugerMsg =
    | ClientToGaugerMsg of ClientToGaugerMsg
    | SimulationToGaugerMsg of SimulationToGaugerMsg

and ToClientMsg =
    | GaugerToClientMsg of GaugerToClientMsg
    | SimulationToClientMsg of SimulationToClientMsg

and ClientToGaugerMsg =
    | PendingRequests of ClientToGaugerPendingRequestsMsg list

and SimulationToGaugerMsg =
    | GaugerTick of Map<ClientName, MailboxProcessor<ToClientMsg>> * int

and GaugerToClientMsg =
    | Apportions of GaugerToClientApportionMsg list

and GaugerToClientApportionMsg = {
    TargetResource : string
    Apportion : float
}

and SimulationToClientMsg =
    | ClientTick of int
    | IncrementRequests of IncrementRequestsEvent

and ClientToGaugerPendingRequestsMsg = {
    Client : string
    Resource : string
    TotalPendingRequests : int
}
