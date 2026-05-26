using Aiursoft.Apkg.Client.Handlers;
using Aiursoft.CommandFramework;
using Aiursoft.CommandFramework.Models;

return await new NestedCommandApp()
    .WithGlobalOptions(CommonOptionsProvider.VerboseOption)
    .WithFeature(new NewHandler())
    .WithFeature(new PackHandler())
    .WithFeature(new PushHandler())
    .WithFeature(new InstallHandler())
    .RunAsync(args);
