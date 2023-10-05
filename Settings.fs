module Settings

// Simulation
let [<Literal>] ResultPath = "result"

// Clients
let [<Literal>] TakeSize = 10.0
let [<Literal>] InitialApportion = 0.1

// Gauger
let [<Literal>] MaxInactiveCycles = 2
let [<Literal>] RefreshPendingRequestsEachCycles = 1
let [<Literal>] RefreshApportionsEachCycles = 1
