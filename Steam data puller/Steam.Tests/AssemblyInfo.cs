// Several command tests capture Console.Out, which is process-global. Running
// test classes in parallel makes one class's output land in another's buffer,
// so the whole assembly runs sequentially. The suite finishes in under a
// second, so there is nothing to gain from parallelism here.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
