using AutoMapper;

internal static class ApplicationExceptionProbe
{
    public static object Run(string dllSha256, string expectedDllSha256)
    {
        if (dllSha256 != expectedDllSha256)
        {
            throw new InvalidOperationException("加载的 AutoMapper DLL 与冻结输入摘要不符。");
        }

        const int injectionInput = 21;
        const int recoveryInput = 55;
        const string marker = "Du.AutoMapper r3.4 application-exception test injection";
        var injected = new InsufficientExecutionStackException(marker);
        var injectionCount = 0;
        IMapper mapper = new MapperConfiguration(config =>
        {
            config.CreateMap<Input, Output>().ConvertUsing((source, _, _) =>
            {
                if (source.Value == injectionInput)
                {
                    injectionCount++;
                    throw injected;
                }
                return new Output(source.Value);
            });
            config.CreateMap<Envelope, EnvelopeDto>();
        }).CreateMapper();

        AutoMapperMappingException observed;
        try
        {
            mapper.Map<EnvelopeDto>(new Envelope(new Input(injectionInput)));
            throw new InvalidOperationException("映射调用方未观察到注入的测试异常。");
        }
        catch (AutoMapperMappingException error)
        {
            observed = error;
        }

        List<string> exceptionChain = [];
        var foundInjectedInstance = false;
        for (Exception current = observed; current != null; current = current.InnerException)
        {
            exceptionChain.Add(current.GetType().FullName);
            foundInjectedInstance |= ReferenceEquals(current, injected);
        }
        if (!foundInjectedInstance || injected.Message != marker || injectionCount != 1)
        {
            throw new InvalidOperationException("包装链缺少唯一注入的测试异常实例。", observed);
        }

        var recovered = mapper.Map<EnvelopeDto>(new Envelope(new Input(recoveryInput))).Item.Value;
        if (recovered != recoveryInput || injectionCount != 1)
        {
            throw new InvalidOperationException("同一 Mapper 的后续映射未得到 55。");
        }

        return new
        {
            pid = Environment.ProcessId,
            cwd = Environment.CurrentDirectory,
            exceptionSource = "test-injection",
            marker,
            exceptionChain,
            foundInjectedInstance,
            injectionCount,
            injectionInput,
            recoveryInput,
            recovered,
        };
    }

    public sealed record Input(int Value);
    public sealed record Output(int Value);
    public sealed record Envelope(Input Item);
    public sealed class EnvelopeDto
    {
        public Output Item { get; set; }
    }
}
