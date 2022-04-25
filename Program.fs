module NvmFs.Main

open FSharp.SystemCommandLine
open System.Threading.Tasks
open NvmFs.Cmd

let installCommand =
    command "install" {
        description "Installs the specified node version or the latest LTS by default"
        inputs (
            Input.ArgumentMaybe<string>("version", "Installs the specified node version"),
            Input.OptionMaybe<bool>(["-l"; "--lts"], "Ignores version and pulls down the latest LTS version"),
            Input.OptionMaybe<bool>(["-c"; "--current"], "Ignores version and pulls down the latest Current version"),
            Input.Option<bool>(["-d"; "--default"], false, "Sets the downloaded version as default")
        )
        setHandler Actions.Install
    }

let uninstallCommand = 
    command "uninstall" {
        description "Uninstalls the specified node version"
        inputs (Input.ArgumentMaybe<string>("version", "Installs the specified node version"))
        setHandler Actions.Uninstall
    }

let useCommand =
    command "use" {
        description "Sets the Node Version"
        inputs (
            Input.ArgumentMaybe<string>("version", "Installs the specified node version"),
            Input.OptionMaybe<bool>(["-l"; "--lts"], "Ignores version and pulls down the latest LTS version"),
            Input.OptionMaybe<bool>(["-c"; "--current"], "Ignores version and pulls down the latest Current version")
        )
        setHandler Actions.Use
    }

let listCommand = 
    command "list" {
        description "Shows the available node versions"
        inputs (
            Input.OptionMaybe<bool>(["-r"; "--remote"], "Displays the last downloaded version index in the console"),
            Input.OptionMaybe<bool>(["-u"; "--update"], "Use together with --remote, pulls the version index from the node website")
        )
        setHandler Actions.List
    }

[<EntryPoint>]
let main argv = 
    rootCommand argv {
        description "nvmfs"
        setHandler Task.FromResult
        addCommand installCommand
        addCommand uninstallCommand
        addCommand useCommand
        addCommand listCommand
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously


//[<EntryPoint>]
//let main argv =

//    let result = Parser.Default.ParseArguments<Install, Use, List, Uninstall>(argv)

//    let result =
//        task {
//            match result with
//            | :? (Parsed<obj>) as cmd ->
//                match cmd.Value with
//                | :? Install as opts -> return! Actions.Install opts
//                | :? Use as opts -> return! Actions.Use opts
//                | :? Uninstall as opts -> return! Actions.Uninstall opts
//                | :? List as opts -> return! Actions.List opts
//                | _ -> return 1
//            | _ -> return 1
//        }
//        |> Async.AwaitTask
//        |> Async.Catch
//        |> Async.RunSynchronously

//    match result with
//    | Choice1Of2 result -> result
//    | Choice2Of2 ex ->
//        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything)
//        1
