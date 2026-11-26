using AutoMapper.Internal.Mappers;

namespace AutoMapper.UnitTests;

public class CollectionMappingContext
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Collection_plan_checks_context_when_the_member_or_element_requires_it(bool memberDepth, bool elementReferences)
    {
        var configuration = new MapperConfiguration(cfg =>
        {
            var document = cfg.CreateMap<Document, DocumentView>();
            if (memberDepth)
            {
                document.MaxDepth(2);
            }
            var line = cfg.CreateMap<Line, LineView>();
            if (elementReferences)
            {
                line.PreserveReferences();
            }
        }).Internal();

        var typeMap = configuration.FindTypeMapFor(typeof(Document), typeof(DocumentView));
        var member = typeMap.PropertyMaps.Single();
        var plan = (BlockExpression)new CollectionMapper().MapExpression(configuration, typeMap.Profile, member,
            Expression.Parameter(typeof(List<Line>)), Expression.Parameter(typeof(List<LineView>)));

        var checks = plan.Expressions.OfType<MethodCallExpression>()
            .Where(call => call.Method.DeclaringType == typeof(ResolutionContext) && call.Method.Name == "CheckContext")
            .ToArray();
        if (memberDepth || elementReferences)
        {
            Assert.Same(checks.Single(), plan.Expressions[0]);
        }
        else
        {
            Assert.Empty(checks);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Collection_update_reuses_destination_and_preserves_shared_elements(bool fromDictionary)
    {
        var configuration = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Document, DocumentView>();
            cfg.CreateMap<Line, LineView>().PreserveReferences();
        });
        configuration.AssertConfigurationIsValid();
        var sharedLine = new Line { Quantity = 3 };
        var source = new Document { Lines = [sharedLine, new() { Quantity = 2 }, sharedLine] };
        var existingLines = new List<LineView> { new() { Quantity = 99 } };
        var destination = new DocumentView { Lines = existingLines };

        var mapper = configuration.CreateMapper();
        var mapped = fromDictionary
            ? mapper.Map(new Dictionary<string, object> { [nameof(Document.Lines)] = source.Lines }, destination)
            : mapper.Map(source, destination);

        mapped.ShouldBeSameAs(destination);
        mapped.Lines.ShouldBeSameAs(existingLines);
        mapped.Lines.Select(line => line.Quantity).ShouldBe(new[] { 3, 2, 3 });
        mapped.Lines[0].ShouldBeSameAs(mapped.Lines[2]);
        mapped.Lines[1].ShouldNotBeSameAs(mapped.Lines[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Section_collection_follows_configured_depth(int maxDepth)
    {
        var configuration = new MapperConfiguration(cfg =>
        {
            var section = cfg.CreateMap<Section, SectionView>();
            if (maxDepth > 0)
            {
                section.MaxDepth(maxDepth);
            }
        });
        configuration.AssertConfigurationIsValid();
        var source = new Section
        {
            Title = "目录",
            Sections = [new() { Title = "商品" }, new() { Title = "价格" }]
        };

        var mapped = configuration.CreateMapper().Map<SectionView>(source);

        mapped.Title.ShouldBe("目录");
        if (maxDepth == 1)
        {
            mapped.Sections.ShouldBeEmpty();
        }
        else
        {
            mapped.Sections.Select(section => section.Title).ShouldBe(new[] { "商品", "价格" });
            Assert.All(mapped.Sections, section => section.Sections.ShouldBeEmpty());
        }
    }

    public class Document
    {
        public List<Line> Lines { get; set; }
    }

    public class DocumentView
    {
        public List<LineView> Lines { get; set; }
    }

    public class Line
    {
        public int Quantity { get; set; }
    }

    public class LineView
    {
        public int Quantity { get; set; }
    }

    public class Section
    {
        public string Title { get; set; }
        public List<Section> Sections { get; set; } = [];
    }

    public class SectionView
    {
        public string Title { get; set; }
        public List<SectionView> Sections { get; set; }
    }
}
