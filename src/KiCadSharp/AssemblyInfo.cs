using System.Runtime.CompilerServices;

// The nng wrapper (KiCadSharp.Interop) is internal: nng is an implementation detail of the
// transport and nothing about it belongs in this library's public surface. The tests still have to
// be able to reach it -- a P/Invoke layer that is only ever exercised through a live KiCad is a
// P/Invoke layer with no gate on the machines that have no KiCad, which is all of CI.
[assembly: InternalsVisibleTo("KiCadSharp.Tests")]
