module NvmFs.Handlers
#nowarn "3391"

open System
open NvmFs.Cmd

let installHandler (version: string option, lts: bool Nullable, current: bool Nullable, isDefault: bool) = 
    Actions.Install
        {
            Install.version = version
            Install.lts = lts
            Install.current = current
            Install.isDefault = isDefault
        }

let uninstallHandler (version: string option) = 
    Actions.Uninstall
        {
            Uninstall.version = version
        }

let useHandler (version: string option, lts: bool Nullable, current: bool Nullable) = 
    Actions.Use
        {
            Use.version = version
            Use.lts = lts
            Use.current = current
        }
    
let listHandler (remote: bool Nullable, updateIndex: bool Nullable) = 
    Actions.List 
        {
            List.remote = remote
            List.updateIndex = updateIndex
        }

