namespace NvmFs

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Threading.Tasks

open CliWrap
open Spectre.Console
open FsToolkit.ErrorHandling

module Network =
  [<Literal>]
  let UserAgent = "NvmFs/1.0"

  let http = lazy (new HttpClient(Timeout = TimeSpan.FromSeconds(5.0)))

  let downloadWithPwsh url fileDestination = task {
    let work =
      Cli
        .Wrap("pwsh")
        .WithArguments(
          [|
            "iwr"
            "-Uri"
            url
            "-UserAgent"
            UserAgent
            "-OutFile"
            fileDestination
          |]
        )
        .WithValidation(CommandResultValidation.ZeroExitCode)

    try
      do! work.ExecuteAsync().Task :> Task
      return Ok()
    with ex ->
      return Error ex
  }

  let downloadWithCurl url fileDestination = task {
    let work =
      Cli
        .Wrap("curl")
        .WithArguments(
          [|
            url
            "--compressed"
            "--fail"
            "-A"
            UserAgent
            "-o"
            fileDestination
          |]
        )
        .WithValidation(CommandResultValidation.ZeroExitCode)

    try
      do! work.ExecuteAsync().Task :> Task
      return Ok()
    with ex ->
      return Error ex
  }

  let downloadWithWGet url fileDestination = task {
    let work =
      Cli
        .Wrap("wget")
        .WithArguments(
          [|
            "--compression=auto"
            "-U"
            UserAgent
            "-O"
            fileDestination
            url
          |]
        )
        .WithValidation(CommandResultValidation.ZeroExitCode)

    try
      do! work.ExecuteAsync().Task :> Task
      return Ok()
    with ex ->
      return Error ex
  }

  let downloadWithExternalTool (url: string) (fileDestination: string) =
    downloadWithCurl url fileDestination
    |> TaskResult.orElseWith(fun error ->
      AnsiConsole.MarkupLine
        $"[yellow]Failed to download node versions: {error.Message.EscapeMarkup()}, retrying...[/]"

      downloadWithPwsh url fileDestination)
    |> TaskResult.orElseWith(fun error ->
      AnsiConsole.MarkupLine
        $"[yellow]Failed to download node versions: {error.Message.EscapeMarkup()}, last attempt...[/]"

      downloadWithWGet url fileDestination)


  let downloadNodeVersions(path: string) =
    let url = $"{Common.getSrcBaseUrl()}/index.json"
    let filepath = Path.Combine(path, "index.json")

    task {
      try
        use! indexStr = http.Value.GetStreamAsync(url)
        use file = File.Create(filepath)
        do! indexStr.CopyToAsync(file)
        return Ok()
      with ex ->
        return Error ex
    }
    |> TaskResult.orElseWith(fun error ->
      AnsiConsole.MarkupLine
        $"[yellow]Failed to download node versions: {error.Message.EscapeMarkup()}, retrying...[/]"

      downloadWithExternalTool url filepath)

  let downloadNode (version: string) (os: CurrentOS) (arch: string) =
    let extension = if os = Windows then ".zip" else ".tar.gz"

    let targetDirectory = Common.getVersionDirName version os arch

    let filename = $"{targetDirectory}{extension}"

    let url = $"{Common.getSrcBaseUrl()}/{version}/{filename}"
    let homePath = Common.getHome()

    let extractedPath = Path.Combine(homePath, targetDirectory)
    let updatedPath = Path.Combine(homePath, version)

    let downloadFile (url: string) path =
      task {
        try
          let! stream = http.Value.GetStreamAsync url
          return Ok stream
        with ex ->
          return Error ex
      }
      |> TaskResult.orElseWith(fun error ->
        AnsiConsole.MarkupLine
          $"[yellow]Failed to download node: {error.Message.EscapeMarkup()}, retrying...[/]"

        downloadWithExternalTool url path
        |> TaskResult.map(fun _ ->
          File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)))

    taskResult {
      AnsiConsole.MarkupLine $"[yellow]Downloading file[/]: {url}"

      use! stream = downloadFile url (Path.Combine(homePath, filename))
      IO.extractFromStream(os, homePath, stream)

      try
        Directory.Delete(updatedPath, true)
      with
      | :? DirectoryNotFoundException
      | :? IOException as ex ->
        Debug.WriteLine("Directory not found: {0}", ex.Message)

      Directory.Move(extractedPath, updatedPath)
      return updatedPath
    }
