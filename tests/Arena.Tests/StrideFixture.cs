using Arena.Engine;
using Arena.Game.HideAndSeek;
using Arena.Rendering;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Arena.Tests;

// Stride's asset provider is process-wide. GPU tests share one device and serialize their work.
[CollectionDefinition("Stride")]
public sealed class StrideCollection : ICollectionFixture<StrideFixture>;

public sealed class StrideFixture : IAsyncLifetime
{
    private StrideHost host = null!;
    private BepuCharacterWorld world = null!;
    private IObservationRenderer renderer = null!;

    public async Task InitializeAsync()
    {
        host = new StrideHost(new StrideRunOptions(Headless: true, Manual: true), (physics, graphics) =>
        {
            world = (BepuCharacterWorld)physics;
            renderer = graphics;
            return new GameSimulation(new HideAndSeekGame(physics), renderer: graphics);
        });
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(90));
    }

    public Task Run(Action<BepuCharacterWorld, IObservationRenderer> test) => host.Dispatcher.InvokeAsync(_ =>
    {
        test(world, renderer);
        return true;
    });

    public async Task DisposeAsync() => await host.DisposeAsync();
}
