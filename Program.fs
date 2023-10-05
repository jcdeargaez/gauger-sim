open System
open System.IO
open System.Threading

open Domain

let log fileName =
    let path = File.Open (Path.Combine (Settings.ResultPath, fileName), FileMode.Create, FileAccess.Write)
    let writer = new StreamWriter (path)
    fun (msg : string) ->
        printfn "%s" msg
        writer.WriteLine msg
        writer.Flush ()

let createGaugerAgent log initialState =
    let processClientMsg gauger msg =
        log "Processing client message"
        match msg with
        | PendingRequests prs ->
            let (nextGlobalPendingClientData, nextGlobalPendingTotals) =
                prs
                |> List.fold (fun (globalClientDataSoFar, globalTotalsSoFar) pr ->
                    let crKey = pr.Client, pr.Resource
                    let prevTotalPendingRequests =
                        globalClientDataSoFar
                        |> Map.tryFind crKey
                        |> Option.map (fun cd -> cd.PendingRequests)
                        |> Option.defaultValue 0

                    let nextPendingClientData =
                        globalClientDataSoFar
                        |> Map.change crKey (function
                            | Some cd ->
                                log $" - Replacing pending requests for '{pr.Client}->{pr.Resource}' from {cd.PendingRequests} to {pr.TotalPendingRequests}"
                                Some {PendingRequests = pr.TotalPendingRequests; LastActivityCycle = gauger.CurrentCycle}
                            | None ->
                                log $" - Placing pending requests for '{pr.Client}->{pr.Resource}' to {pr.TotalPendingRequests}"
                                Some {PendingRequests = pr.TotalPendingRequests; LastActivityCycle = gauger.CurrentCycle})
                    
                    let nextPendingTotals =
                        globalTotalsSoFar
                        |> Map.change pr.Resource (function
                            | Some prevGlobalTotal ->
                                let nextGlobalTotal = prevGlobalTotal - prevTotalPendingRequests + pr.TotalPendingRequests
                                log $" - Changing total pending requests for '{pr.Resource}' from {prevGlobalTotal} to {nextGlobalTotal}"
                                Some nextGlobalTotal
                            | None ->
                                log $" - Adding {pr.TotalPendingRequests} total pending requests for '{pr.Resource}'"
                                Some pr.TotalPendingRequests)

                    nextPendingClientData, nextPendingTotals) (gauger.GlobalPendingClientData, gauger.GlobalPendingTotals)

            log String.Empty

            {gauger with
                GlobalPendingClientData = nextGlobalPendingClientData
                GlobalPendingTotals = nextGlobalPendingTotals}
    
    let processSimulationMsg gauger msg =
        match msg with
        | GaugerTick (clients, cycle) ->
            log $"GaugerTick {cycle}"
            let nextGlobalPendingClientData =
                gauger.GlobalPendingClientData
                |> Map.filter (fun (client, resource) cd ->
                    let active = cd.LastActivityCycle + Settings.MaxInactiveCycles > gauger.CurrentCycle
                    if active then log $" - '{client}->{resource}' may still be active"
                    else log $" - '{client}->{resource}' dropped"
                    active)
            
            let nextGlobalPendingTotals =
                nextGlobalPendingClientData
                |> Map.fold (fun pendingTotalsSoFar (client, resource) cd ->
                    pendingTotalsSoFar
                    |> Map.change resource (Option.defaultValue 0 >> (+) cd.PendingRequests >> Some)) Map.empty

            if cycle % Settings.RefreshApportionsEachCycles = 0 then
                nextGlobalPendingClientData
                |> Map.fold (fun clientApportionsSoFar (client, resource) gd ->
                    let resourceTotal = nextGlobalPendingTotals |> Map.find resource
                    let apportion = float gd.PendingRequests / float resourceTotal
                    let apportionMsg = {TargetResource = resource; Apportion = apportion}
                    log $" - Apportion %.2f{apportion} ({gd.PendingRequests}/{resourceTotal}) '{client}->{resource}'"
                    
                    clientApportionsSoFar
                    |> Map.change client (function
                        | Some msgs -> apportionMsg :: msgs |> Some
                        | None -> Some [apportionMsg])) Map.empty
                |> Map.iter (fun client apportions ->
                    let client = clients |> Map.find client
                    apportions
                    |> Apportions
                    |> GaugerToClientMsg
                    |> client.Post)

            log String.Empty

            {gauger with
                CurrentCycle = cycle
                GlobalPendingClientData = nextGlobalPendingClientData
                GlobalPendingTotals = nextGlobalPendingTotals}

    let processMsg gauger = function
        | ClientToGaugerMsg msg -> processClientMsg gauger msg
        | SimulationToGaugerMsg msg -> processSimulationMsg gauger msg
    
    MailboxProcessor.Start (fun inbox ->
        let rec messageLoop gaugerSoFar = async {
            let! msg = inbox.Receive ()
            let nextGauger = processMsg gaugerSoFar msg
            return! messageLoop nextGauger
        }
        messageLoop initialState)

let createClientAgent log (gauger : MailboxProcessor<ToGaugerMsg>) resources initialState =
    let processGaugerMsg client msg =
        log $"{client.Name} processing gauger message"
        match msg with
        | Apportions ams ->
            let nextResources =
                ams
                |> Seq.map (fun am ->
                    log $" - Gauger indicated apportion %.2f{am.Apportion} to resource '{am.TargetResource}'"
                    let crd = client.Resources |> Map.find am.TargetResource
                    am.TargetResource, {crd with Apportion = am.Apportion})
                |> Map
            
            log String.Empty

            {client with Resources = nextResources}
    
    let processSimulationMsg (client : Client) msg =
        match msg with
        | ClientTick cycle ->
            log $"ClientTick {cycle}"
            let nextResources =
                client.Resources
                |> Map.map (fun resource crd ->
                    let takeSize =
                        match resources |> Map.find resource with
                        | CpuResource cr ->
                            let cpuAvailability = (100.0 - float cr.Cpu) / 100.0
                            log $"'{resource}' CPU availability %.2f{cpuAvailability}"
                            Settings.TakeSize * cpuAvailability
                        | LimitedResource lr ->
                            log $"'{resource}' max rate per cycle  {lr.MaxRatePerCycle}"
                            float lr.MaxRatePerCycle

                    let apportedTakeSize = takeSize * crd.Apportion |> int
                    let taken = min crd.PendingRequests (max apportedTakeSize 1)
                    let nextRequests = crd.PendingRequests - taken
                    log $" - Taking {taken} requests for '{resource}', had {crd.PendingRequests} pending requests, now {nextRequests}"
                    {crd with PendingRequests = nextRequests})
                |> Map.filter (fun resource crd ->
                    if crd.PendingRequests = 0 then
                        log $" - Finished pending requests to '{resource}'"
                    crd.PendingRequests > 0)
            
            // Notify pending requests to gauger
            if cycle % Settings.RefreshPendingRequestsEachCycles = 0 then
                let pendingRequests =
                    nextResources
                    |> Map.toSeq
                    |> Seq.map (fun (resource, crd) ->
                        log $" - Refreshing pending requests '{client.Name}->{resource}' {crd.PendingRequests}"
                        {Client = client.Name; Resource = resource; TotalPendingRequests = crd.PendingRequests})
                    |> Seq.toList

                if pendingRequests |> List.isEmpty |> not then
                    pendingRequests
                    |> PendingRequests
                    |> ClientToGaugerMsg
                    |> gauger.Post

            log String.Empty

            {client with Resources = nextResources}

        | IncrementRequests increment ->
            log $"Incrementing pending requests to resource '{increment.Resource}' by {increment.RequestsIncrement}"
            let nextResources =
                client.Resources
                |> Map.change increment.Resource (function
                    | Some rd ->
                        let nextPendingRequests = rd.PendingRequests + increment.RequestsIncrement
                        log $" - Had {rd.PendingRequests} now {nextPendingRequests} with current apportion {rd.Apportion}"
                        Some {rd with PendingRequests = nextPendingRequests}
                    | None ->
                        log $" - Now {increment.RequestsIncrement} with initial apportion {Settings.InitialApportion}"
                        Some {PendingRequests = increment.RequestsIncrement; Apportion = Settings.InitialApportion; TargetResource = resources |> Map.find increment.Resource})
            
            log String.Empty

            {client with Resources = nextResources}

    let processMsg client = function
        | GaugerToClientMsg msg -> processGaugerMsg client msg
        | SimulationToClientMsg msg -> processSimulationMsg client msg

    MailboxProcessor.Start (fun inbox ->
        let rec messageLoop clientSoFar = async {
            let! msg = inbox.Receive ()
            let nextClient = processMsg clientSoFar msg
            return! messageLoop nextClient
        }
        messageLoop initialState)

let tick (gauger : MailboxProcessor<ToGaugerMsg>) (clients : Map<string, MailboxProcessor<ToClientMsg>>) (events : Map<int, IncrementRequestsEvent list>) cycle =
    printfn $"Tick {cycle}"
    
    // Notify simulation events to clients
    events
    |> Map.tryFind cycle
    |> Option.iter (fun events ->
        events
        |> List.iter (fun event ->
            match clients |> Map.tryFind event.Client with
            | Some client -> IncrementRequests event |> SimulationToClientMsg |> client.Post
            | None -> failwith $"Client '{event.Client}' from event not found in cycle {cycle}"))
    
    // Notify tick to gauger
    GaugerTick (clients, cycle) |> SimulationToGaugerMsg |> gauger.Post
    
    // Notify tick to clients
    clients
    |> Map.iter (fun _ client -> ClientTick cycle |> SimulationToClientMsg |> client.Post)

    Thread.Sleep 500

let simulate configuration =
    let initialCycle = 1
    let initialGaugerState = {CurrentCycle = initialCycle; GlobalPendingClientData = Map.empty; GlobalPendingTotals = Map.empty}
    let gauger = createGaugerAgent (log "gauger.txt") initialGaugerState

    let resources =
        configuration.Resources
        |> Seq.map (fun resource ->
            let name =
                match resource with
                | LimitedResource lr -> lr.LimitedResourceName
                | CpuResource cr -> cr.CpuResourceName
            name, resource)
        |> Map
    
    let clients =
        configuration.Clients
        |> Seq.map (fun name -> name, createClientAgent (log $"{name}.txt") gauger resources {Name = name; Resources = Map.empty})
        |> Map

    let simulateCycle = tick gauger clients configuration.Events

    {initialCycle..configuration.Cycles}
    |> Seq.iter simulateCycle

simulate Configuration.custom
