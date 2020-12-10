namespace NvmFs

open FSharp.Control.Tasks
open CommandLine
open NvmFs.Cmd

module Main =

    [<EntryPoint>]
    let main argv =

        let result =
            Parser.Default.ParseArguments<Install, Use, List, Uninstall>(argv)

        match result with
        | :? (Parsed<obj>) as cmd ->
            match cmd.Value with
            | :? Install as opts ->
                Actions.Install opts
                |> Async.AwaitTask
                |> Async.RunSynchronously
            | :? Use as opts -> Actions.Use opts
            | :? Uninstall as opts -> Actions.Uninstall opts
            | :? List as opts -> Actions.List opts
            | _ -> 1
        | _ -> 1
