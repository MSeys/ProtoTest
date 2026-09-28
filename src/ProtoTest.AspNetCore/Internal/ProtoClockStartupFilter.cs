namespace ProtoTest.AspNetCore.Internal;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using ProtoTest.Core;

/// <summary>
/// Pushes the clock of the test that caused a request while the application handles it. ASP.NET Core
/// does not carry the test's ambient context into the request pipeline, but it does carry the test id
/// the in-process client sends, so the exchange is linked by that. The registry belongs to the host
/// that started this application, so two hosts sharing a test id each push their own clock.
/// </summary>
internal sealed class ProtoClockStartupFilter(ProtoClockRegistry clockRegistry) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
    {
        application.Use(async (context, nextMiddleware) =>
        {
            var testId = context.Request.Headers[ProtoTestContextPropagation.TestIdHeader].ToString();
            var clock = string.IsNullOrEmpty(testId) ? null : clockRegistry.Find(testId);
            if (clock is null)
            {
                await nextMiddleware().ConfigureAwait(false);
                return;
            }

            using (ProtoRequestClock.Push(clock))
            {
                await nextMiddleware().ConfigureAwait(false);
            }
        });

        next(application);
    };
}
