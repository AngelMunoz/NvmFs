module NvmFs.Main

open FSharp.SystemCommandLine
open System.Threading.Tasks
open NvmFs.Cmd

let installCommand =
    let version = Input.ArgumentMaybe<string>("version", "Installs the specified node version")
    let lts = Input.OptionMaybe<bool>(["-l"; "--lts"], "Ignores version and pulls down the latest LTS version")
    let current = Input.OptionMaybe<bool>(["-c"; "--current"], "Ignores version and pulls down the latest Current version")
    let isDefault = Input.Option<bool>(["-d"; "--default"], false, "Sets the downloaded version as default")

    command "install" {
        description "Installs the specified node version or the latest LTS by default"
        inputs (version, lts, current, isDefault)
        setHandler (fun (version, lts, current, isDefault) ->
            Actions.Install { version = version; lts = lts; current = current; isDefault = isDefault }
        )
    }

let uninstallCommand = 
    let version = Input.ArgumentMaybe<string>("version", "Installs the specified node version")

    command "uninstall" {
        description "Uninstalls the specified node version"
        inputs version
        setHandler (fun (version) ->
            Actions.Uninstall { version = version }
        )
    }

let useCommand =
    let version = Input.ArgumentMaybe<string>("version", "Installs the specified node version")
    let lts = Input.OptionMaybe<bool>(["-l"; "--lts"], "Ignores version and pulls down the latest LTS version")
    let current = Input.OptionMaybe<bool>(["-c"; "--current"], "Ignores version and pulls down the latest Current version")

    command "use" {
        description "Sets the Node Version"
        inputs (version, lts, current)
        setHandler (fun (version, lts, current) ->
            Actions.Use { version = version; lts = lts; current = current }
        )
    }

let listCommand = 
    let remote = Input.OptionMaybe<bool>(["-r"; "--remote"], "Displays the last downloaded version index in the console")
    let updateIndex = Input.OptionMaybe<bool>(["-u"; "--update"], "Use together with --remote, pulls the version index from the node website")

    command "list" {
        description "Shows the available node versions"
        inputs (remote, updateIndex)
        setHandler (fun (remote, updateIndex) ->
            Actions.List { remote = remote; updateIndex = updateIndex }
        )
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
