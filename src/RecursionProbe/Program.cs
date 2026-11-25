using System.Dynamic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AutoMapper;

var engine = typeof(Mapper).Assembly;
var scenario = args.Single();
var result = scenario switch
{
    "reference-graph" => ReferenceGraph(),
    "constructor-depth" => ConstructorDepth(),
    "runtime-dispatch" => RuntimeDispatch(),
    _ => throw new ArgumentException(scenario),
};
Console.WriteLine(JsonSerializer.Serialize(new
{
    scenario,
    result,
    runtime = RuntimeInformation.FrameworkDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    assembly = engine.FullName,
    location = engine.Location,
    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(engine.Location))).ToLowerInvariant(),
}));

static object ReferenceGraph()
{
    var automatic = new MapperConfiguration(cfg => cfg.CreateMap<Node, NodeDto>()).CreateMapper();
    var preserve = new MapperConfiguration(cfg => cfg.CreateMap<Node, NodeDto>().PreserveReferences()).CreateMapper();
    var length = new List<int>();
    foreach (var mapper in new[] { automatic, preserve })
    {
        var tree = Node.Chain(65);
        var bounded = Count(mapper.Map<NodeDto>(tree), item => item.Next);
        Require(bounded == 64, $"默认深度实际为 {bounded}");
        length.Add(bounded);
        var cycle = new Node { Value = 3 };
        cycle.Next = cycle;
        var cycleDto = mapper.Map<NodeDto>(cycle);
        Require(cycleDto.Value == 3 && ReferenceEquals(cycleDto, cycleDto.Next), "自引用身份改变");
        var existing = new NodeDto();
        Require(ReferenceEquals(existing, mapper.Map(new Node { Value = 5 }, existing)) && existing.Value == 5,
            "普通映射未更新已有对象");
    }
    return new { lengths = length, sameCycleReference = true, sameExistingDestination = true };
}

static object ConstructorDepth()
{
    var explicitLimit = new MapperConfiguration(cfg =>
    {
        cfg.ShouldUseConstructor = ctor => ctor.IsPublic;
        cfg.CreateMap<Node, ImmutableNode>().MaxDepth(2);
        cfg.CreateMap<Node, ConstructedNode>().MaxDepth(2)
            .ConstructUsing((source, context) => ConstructedNode.Create(source, context));
        cfg.CreateMap<Node, ConstructorContextResult>()
            .ConvertUsing((source, _, context) => ConstructorContextResult.Create(source, context));
        cfg.CreateMap<ThrowDuringConstruction, CheckedConstruction>().MaxDepth(1)
            .ConstructUsing(source => new CheckedConstruction(source.Value));
    }).CreateMapper();
    var automatic = new MapperConfiguration(cfg =>
    {
        cfg.ShouldUseConstructor = ctor => ctor.IsPublic;
        cfg.CreateMap<Node, ImmutableNode>();
    }).CreateMapper();
    var chain = Node.Chain(3);
    var positional = Count(explicitLimit.Map<ImmutableNode>(chain), item => item.Next);
    var constructed = Count(explicitLimit.Map<ConstructedNode>(chain), item => item.Next);
    var defaultPositional = Count(automatic.Map<ImmutableNode>(Node.Chain(65)), item => item.Next);
    Require(positional == 2 && constructed == 2 && defaultPositional == 64, "构造阶段深度边界改变");
    var context = explicitLimit.Map<Node, ConstructorContextResult>(new Node { Value = 21 }, _ => { });
    Require(context == new ConstructorContextResult(nameof(ArgumentOutOfRangeException), 21),
        "构造失败后，同一上下文未能继续映射");
    var after = explicitLimit.Map<ImmutableNode>(new Node { Value = 34 });
    Require(after.Value == 34 && after.Next == null, "同一个 Mapper 后续构造映射失败");
    return new { positional, constructed, defaultPositional, context.Exception, context.Recovered, subsequentValue = after.Value };
}

static object RuntimeDispatch()
{
    var mapper = new MapperConfiguration(cfg =>
    {
        cfg.CreateMap<Node, NodeDto>().Include<SpecialNode, SpecialNodeDto>().MaxDepth(2);
        cfg.CreateMap<SpecialNode, SpecialNodeDto>();
        cfg.CreateMap<InterfaceNode, NodeDto>();
        cfg.CreateMap<ObjectNode, NodeDto>();
        cfg.CreateMap<Node, ConvertedNode>().MaxDepth(2)
            .ConvertUsing((source, _, context) => ConvertedNode.Create(source, context));
    }).CreateMapper();
    Node polymorphic = new SpecialNode { Next = new SpecialNode { Next = new SpecialNode() } };
    var derived = mapper.Map<Node, NodeDto>(polymorphic);
    var inheritedDepth = Count(derived, item => item.Next);
    Require(derived is SpecialNodeDto && inheritedDepth == 2, "派生映射未继承基类深度");
    var defaultLimit = new MapperConfiguration(cfg =>
    {
        cfg.CreateMap<SpecialNode, SpecialNodeDto>();
        cfg.CreateMap<Node, NodeDto>().Include<SpecialNode, SpecialNodeDto>();
    }).CreateMapper();
    Node derivedChain = null;
    for (int i = 0; i < 65; i++)
    {
        derivedChain = new SpecialNode { Value = i, Next = derivedChain };
    }
    var inheritedDefault = Count(defaultLimit.Map<Node, NodeDto>(derivedChain), item => item.Next);
    Require(inheritedDefault == 64, "派生映射未继承默认深度");
    var interfaceResult = mapper.Map<NodeDto>(new InterfaceNode { Next = new InterfaceNode { Value = 8 } });
    var objectResult = mapper.Map<NodeDto>(new ObjectNode { Next = new ObjectNode { Value = 13 } });
    var dictionaryResult = mapper.Map<NodeDto>(new Dictionary<string, object>
        { ["Next"] = new Dictionary<string, object> { ["Value"] = 21 } });
    var dynamicResult = mapper.Map<NodeDto>(new DynamicNode(new DynamicNode(null, 34), 0));
    IDictionary<string, object> expando = new ExpandoObject();
    IDictionary<string, object> expandoChild = new ExpandoObject();
    expandoChild["Value"] = 89;
    expando["Next"] = expandoChild;
    var expandoResult = mapper.Map<NodeDto>(expando);
    var converterDepth = Count(mapper.Map<ConvertedNode>(Node.Chain(3)), item => item.Next);
    Require(interfaceResult.Next.Value == 8 && objectResult.Next.Value == 13 && dictionaryResult.Next.Value == 21
        && dynamicResult.Next.Value == 34 && expandoResult.Next.Value == 89 && converterDepth == 2, "运行时类型或转换器映射改变");

    string converterException;
    try
    {
        mapper.Map<ConvertedNode>(new Node { Value = -1 });
        throw new InvalidOperationException("预期的转换异常未传播");
    }
    catch (ArgumentOutOfRangeException error)
    {
        converterException = error.GetType().Name;
    }
    var recovered = mapper.Map<ConvertedNode>(new Node { Value = 55 }).Value;
    Require(recovered == 55, "同一个 Mapper 后续转换失败");
    return new { inheritedDepth, inheritedDefault, interfaceValue = 8, objectValue = 13, dictionaryValue = 21,
        dynamicValue = 34, expandoValue = 89, converterDepth, converterException, recovered };
}

static int Count<T>(T first, Func<T, T> next) where T : class
{
    var count = 0;
    for (var node = first; node != null; node = next(node))
    {
        Require(++count <= 65, "超过合成用例的输入大小");
    }
    return count;
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

public class Node
{
    public int Value { get; init; }
    public Node Next { get; set; }
    public static Node Chain(int length)
    {
        Node root = null;
        for (int i = 0; i < length; i++)
        {
            root = new() { Value = i + 1, Next = root };
        }
        return root;
    }
}

public class NodeDto
{
    public int Value { get; set; }
    public NodeDto Next { get; set; }
}

public sealed record ImmutableNode(int Value, ImmutableNode Next);
public sealed record ConstructedNode(int Value, ConstructedNode Next)
{
    public static ConstructedNode Create(Node source, ResolutionContext context) =>
        new(source.Value, source.Next == null ? null : context.Mapper.Map<ConstructedNode>(source.Next));
}
public sealed record ConvertedNode(int Value, ConvertedNode Next)
{
    public static ConvertedNode Create(Node source, ResolutionContext context)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(source.Value);
        return new(source.Value, source.Next == null ? null : context.Mapper.Map<ConvertedNode>(source.Next));
    }
}
public sealed record ThrowDuringConstruction(int Value);
public sealed class CheckedConstruction
{
    public int Value { get; }
    public CheckedConstruction(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }
}
public sealed record ConstructorContextResult(string Exception, int Recovered)
{
    public static ConstructorContextResult Create(Node source, ResolutionContext context)
    {
        try
        {
            context.Mapper.Map<CheckedConstruction>(new ThrowDuringConstruction(-1));
            throw new InvalidOperationException("预期的构造异常未传播");
        }
        catch (ArgumentOutOfRangeException error)
        {
            var next = context.Mapper.Map<CheckedConstruction>(new ThrowDuringConstruction(source.Value));
            return new(error.GetType().Name, next?.Value ?? -1);
        }
    }
}
public sealed class SpecialNode : Node;
public sealed class SpecialNodeDto : NodeDto;
public interface INode;
public sealed class InterfaceNode : INode
{
    public int Value { get; init; }
    public INode Next { get; init; }
}
public sealed class ObjectNode
{
    public int Value { get; init; }
    public object Next { get; init; }
}
public sealed class DynamicNode(DynamicNode next, int value) : DynamicObject
{
    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        result = binder.Name == nameof(NodeDto.Next) ? next : value;
        return binder.Name is nameof(NodeDto.Next) or nameof(NodeDto.Value);
    }
}
