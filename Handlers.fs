module NvmFs.Handlers
#nowarn "3391"

open System
open NvmFs.Cmd

let installHandler (version: string, lts: bool Nullable, current: bool Nullable, isDefault: bool) = 
    Actions.Install
        {
            Install.version = version
            Install.lts = lts
            Install.current = current
            Install.isDefault = isDefault
        }
