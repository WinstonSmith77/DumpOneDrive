module DumpOneDrive.Common

open System
open System.IO
open System.Threading.Tasks

let getResult (task: Task<'a>) = task.GetAwaiter().GetResult()

let dump a =
    Console.WriteLine(a.ToString())
    a

let dumpIgnore a = (dump a) |> ignore

let enforcePathExists (path:string) = Directory.CreateDirectory(Path.GetDirectoryName(path)) |> ignore