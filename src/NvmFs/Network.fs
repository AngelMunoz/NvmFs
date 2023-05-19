namespace NvmFs

open System.IO
open System.Net.Http
open Spectre.Console

module Network =

    let http = lazy (new HttpClient())

    let downloadNodeVersions (path: string) =
        task {
            let url = $"{Common.getSrcBaseUrl ()}/index.json"

            use! indexStr = http.Value.GetStreamAsync(url)
            use file = File.Create(Path.Combine(path, "index.json"))

            do! indexStr.CopyToAsync(file)
            return file.Name
        }

    let downloadNode (version: string) (os: CurrentOS) (arch: string) =
        task {
            let extension = if os = Windows then ".zip" else ".tar.gz"

            let targetDirectory = Common.getVersionDirName version os arch

            let filename = $"{targetDirectory}{extension}"

            let url = $"{Common.getSrcBaseUrl ()}/{version}/{filename}"

            AnsiConsole.MarkupLine $"[yellow]Downloading file[/]: {url}"

            use! str = http.Value.GetStreamAsync url

            let homePath = Common.getHome ()
            let extractedPath = Path.Combine(homePath, targetDirectory)
            let updatedPath = Path.Combine(homePath, version)

            do IO.extractFromStream (os, homePath, str)
            do Directory.Move(extractedPath, updatedPath)
            return updatedPath
        }
