﻿module Main

open DarkMadness2.Client.Console
open DarkMadness2.NetworkCommunication
open DarkMadness2.NetworkCommunication.Serializer
open DarkMadness2.Core
open DarkMadness2.Core.EventSource.EventSourceUtils

let random = System.Random ()
let mutable charPosition = (random.Next 10, random.Next 10)
let mutable otherCharPosition = (0, 0)
let mutable clientId = -1
let communicator = Client ("127.0.0.1", 8181)

let (+) (x1, y1) (x2, y2) = (x1 + x2, y1 + y2)

let processKeyPress (key : System.ConsoleKey) =
    if otherCharPosition <> charPosition then
        putChar ' ' charPosition
    let delta = match key with
                | System.ConsoleKey.UpArrow -> (0, -1)
                | System.ConsoleKey.DownArrow -> (0, 1)
                | System.ConsoleKey.LeftArrow -> (-1, 0)
                | System.ConsoleKey.RightArrow -> (1, 0)
                | _ -> (0, 0)
    let correctCoords (x, y) =
        x >= 0 && x < maxX () && y >= 0 && y <= maxY ()
    let newCoords = charPosition + delta
    if correctCoords newCoords then
        charPosition <- newCoords
    putChar '@' charPosition
    CharacterMoveRequest charPosition |> serialize |> communicator.Send

let processNewCoords coords = 
    if otherCharPosition <> charPosition then
        putChar ' ' otherCharPosition
    otherCharPosition <- coords
    putChar '@' otherCharPosition

// maximizeWindow ()
hideCursor ()

// Connecting to server...
communicator.Send <| serialize ConnectionRequest
let response = receive communicator |> deserialize
match response with 
| ConnectionResponse id -> clientId <- id
| _ -> failwith "Connection failed"

System.Console.Title <- sprintf "Dark Madness 2 {Connected, client id = %d}" clientId

// Done.
putChar '@' charPosition

CharacterMoveRequest charPosition |> serialize |> communicator.Send

type Event = 
| ClientEvent of System.ConsoleKey
| ServerEvent of Message

let processEvent event =
    match event with
    | ClientEvent System.ConsoleKey.Escape -> false
    | ClientEvent key -> 
        processKeyPress key
        true
    | ServerEvent msg -> 
        match msg with 
        | CharacterPositionUpdate (id, x, y) ->
            if id <> clientId then
                processNewCoords (x, y)
            true
        | _ -> true

let consoleEventSource = DarkMadness2.Core.EventSource.SyncEventSourceWrapper (fun () -> System.Console.ReadKey true) 

let eventSource = 
    combine (event communicator) (event consoleEventSource) (
        function
        | Choice1Of2 messageFromServer -> ServerEvent (messageFromServer |> deserialize)
        | Choice2Of2 keyInfo -> ClientEvent keyInfo.Key
    )

type Locker () =
    let autoResetEvent = new System.Threading.AutoResetEvent false
    
    member this.Down () =
        autoResetEvent.Set () |> ignore

    member this.Wait () =
        autoResetEvent.WaitOne () |> ignore

type EventDispatcher<'a> (locker : Locker) =
    let eventBuffer = System.Collections.Generic.Queue<_> ()

    member this.Listener event = 
        eventBuffer.Enqueue event
        locker.Down ()

    member this.GetEvent () = eventBuffer.Dequeue ()

let eventLoop (handleEvent : 'a -> bool) (event : IEvent<'a>) =
    let locker = Locker ()
    let dispatcher = EventDispatcher locker
    event.Add <| dispatcher.Listener
    let rec doLoop () =
        locker.Wait ()
        let event = dispatcher.GetEvent ()
        if handleEvent event then
            doLoop ()
    doLoop ()

eventSource |> eventLoop processEvent
