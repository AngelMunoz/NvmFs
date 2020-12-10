namespace NvmFs

open System.Net.Http
open FSharp.Control.Tasks
open IO
open Spectre.Console

module Network =

    let downloadNodeVersions (path: string) =
        task {
            let url = $"{Common.getSrcBaseUrl ()}/index.json"
            use http = new HttpClient()
            use! indexStr = http.GetStreamAsync(url)

            use index =
                openStreamForPath (fullPath (path, [ "index.json" ]))

            do! indexStr.CopyToAsync(index)
            return index.Name
        }

    let downloadChecksumsForVersion (codename: string) =
        task {
            let url =
                $"{Common.getSrcBaseUrl ()}/{codename}/SHASUMS256.txt"

            use http = new HttpClient()
            use! checksumsStr = http.GetStreamAsync(url)

            use checksums =
                let path =
                    IO.fullPath (Common.getHome (), [ codename; "SHASUMS256.txt" ])

                openStreamForPath path

            do! checksumsStr.CopyToAsync(checksums)
            return checksums.Name
        }

    let downloadNode (codename: string) (version: string) (os: string) (arch: string) =
        task {
            let extension = if os = "win" then ".zip" else ".tar.gz"
            let filename = $"node-{version}-{os}-{arch}{extension}"

            let url =
                $"{Common.getSrcBaseUrl ()}/{codename}/{filename}"

            AnsiConsole.MarkupLine $"[#5f5f00]Downloading file[/]: {url}"
            use http = new HttpClient()
            use! str = http.GetStreamAsync(url)

            use file =
                let path =
                    IO.fullPath (Common.getHome (), [ codename; filename ])

                openStreamForPath path

            do! str.CopyToAsync(file)
            return file.Name
        }
