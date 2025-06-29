namespace NvmFs

open System
open System.IO

open Spectre.Console

open FsToolkit.ErrorHandling

module Env =

  let setNvmFsNodeWin(home: string) =
    let isEnvThere =
      Environment.GetEnvironmentVariable(
        Common.EnvVars.NvmFsNode,
        EnvironmentVariableTarget.User
      )
      |> Option.ofObj

    match isEnvThere with
    | Some value ->
      let path =
        TextPath(path = value)
          .SeparatorColor(Color.Yellow)
          .RootColor(Color.Yellow)

      AnsiConsole.MarkupInterpolated
        $"[yellow]%%{Common.EnvVars.NvmFsNode}%%[/] is already set to: "

      AnsiConsole.Write path
      AnsiConsole.Write('\n')

      Ok()
    | None ->
      let nvmfshome = Path.GetFullPath home
      let nvmfsnode = IO.SymLinkTarget
      let varHome = $"%%{Common.EnvVars.NvmFsHome}%%"
      let varNode = $"%%{Common.EnvVars.NvmFsNode}%%"

      AnsiConsole.MarkupLineInterpolated
        $"Adding [bold yellow]{varHome}[/], [bold yellow]{varNode}[/] to the user env variables."

      Environment.SetEnvironmentVariable(
        Common.EnvVars.NvmFsNode,
        nvmfsnode,
        EnvironmentVariableTarget.User
      )

      Environment.SetEnvironmentVariable(
        Common.EnvVars.NvmFsHome,
        nvmfshome,
        EnvironmentVariableTarget.User
      )

      AnsiConsole.MarkupLine "We've set the env variables to the current user"

      AnsiConsole.MarkupLine
        "Please open  [yellow]SystemPropertiesAdvanced.exe[/]"

      AnsiConsole.MarkupLine
        $"Click on the 'Environment Variables...' button and add '{varNode}' to you user's PATH"

      AnsiConsole.MarkupLine
        "Close your current terminal or log out/log in for it to make effect."

      AnsiConsole.MarkupLineInterpolated
        $"ex. C:\\DirA\\bin;C:\\dir b\\tools\\bin;[bold yellow]%%{Common.EnvVars.NvmFsNode}%%\\bin[/];"

      Error "Needs To Manually Set Environment Variables"

  let setNvmFsNodeUnix isMac = result {
    let nodepath = IO.SymLinkTarget

    let lines = [
      Common.StartMarker
      "# Please do not remove the marker above to avoid re-appending these lines"
      $"export %s{Common.EnvVars.NvmFsNode}=%s{nodepath}"
      $"export PATH=$%s{Common.EnvVars.NvmFsNode}:$PATH"
      Common.EndMarker
    ]

    do! IO.tryUpdateBashrc(lines, isMac)

    Environment.SetEnvironmentVariable(
      Common.EnvVars.NvmFsNode,
      nodepath,
      EnvironmentVariableTarget.User
    )

    Environment.SetEnvironmentVariable(
      Common.EnvVars.NvmFsHome,
      Common.getHome(),
      EnvironmentVariableTarget.User
    )

    AnsiConsole.MarkupLineInterpolated
      $"[yellow]We've set the env variables |${Common.EnvVars.NvmFsNode}|${Common.EnvVars.NvmFsHome}| to the current user[/]"

    AnsiConsole.MarkupLine
      "[yellow]It is likely that you need to restart your terminal for it to take effect.[/]"
  }

  let setEnvVersion (os: CurrentOS) (version: string) =
    let home = Common.getHome()

    result {
      IO.removeSymlink IO.SymLinkTarget |> Result.ignoreError

      match os with
      | Windows -> setNvmFsNodeWin home
      | Mac -> setNvmFsNodeUnix true
      | _ -> setNvmFsNodeUnix false
      |> Result.ignoreError

      let target = IO.SymLinkTarget
      let source = IO.versionDirectory os version
      return! IO.createSymlink source target
    }
