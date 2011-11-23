== Directed Acyclic Word Graph ==

A [[http://en.wikipedia.org/wiki/Directed_acyclic_word_graph|DAWG]] is a data structure for storing large sets of character sequences, usually individual words. DAWG makes a good use of the redundancy in word sets, typically requiring considerably less memory than a plain list of words would, while enabling highly efficient lookups to test if a specific word is in the graph.

=== OnlineDAWG ===

This library exposes two major classes: {{{DawgGraph}}} and {{{DawgGraphReadonly}}}. The former is used to construct a graph by adding one word at a time, and can be saved to a file. The latter does not allow adding new words, but uses much less memory. Both support loading from a file and checking whether a word is in the graph.

=== Efficiency ===

A sorted dictionary of 180,000 English words can be turned into a {{{DawgGraph}}} in less than half a second, and consumes roughly 3 MB of memory. A {{{DawgGraphReadonly}}} with the same set of words consumes just under 1 MB of memory.

An unsorted collection of 98 million domain registrations can be imported into a {{{DawgGraph}}} in a couple of hours and requires roughly 6 GB on a 64-bit machine at peak usage. Saved to disk, this graph takes 510 MB of disk space. Loaded to a {{{DawgGraphReadonly}}}, just over 1 GB of RAM is consumed - well within the capabilities of a 32-bit machine.

For comparison, a {{{string[]}}} of the 98 million domain names consumes 2.5 GB in character data alone. Additionally, the .NET string instance overhead, which would total 1.5 GB on a 32-bit platform, means that this data cannot fit into the address space of a 32-bit process, requiring the use of a 64-bit machine and doubling the overhead to 3 GB, for a total of 5.5 GB.

=== Example ===

{{{
#!csharp

var g = new DawgGraph();
g.Add("graphs");
g.Add("are");
g.Add("cool");
g.Contains("graph"); // true
g.Save("graph.dawg");

var gr = DawgGraphReadonly.Load("graph.dawg");
gr.Contains("cool"); // true
}}}

=== License ===

This library is available to anyone under the [[https://bitbucket.org/rstarkov/onlinedawg/raw/f544c17cb6c3/license-gpl.txt|GPL]]. If you require this library under a different license, please [[Contact|contact the author]]. If you require a specific feature or customization, this can also be arranged.