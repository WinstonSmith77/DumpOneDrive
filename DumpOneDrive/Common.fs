module DumpOneDrive.Common

open System
open System.Threading.Tasks

let getResult (task: Task<'a>) = task.GetAwaiter().GetResult()

let dump a =
    Console.WriteLine(a.ToString())
    a

let dumpIgnore a = (dump a) |> ignore
