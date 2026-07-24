using System.Reflection;
using SolidarityGrid.Application;
using SolidarityGrid.Contracts;
using SolidarityGrid.Domain;
using SolidarityGrid.Infrastructure;
using Xunit;

namespace SolidarityGrid.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void DomainHasNoForbiddenReferences()
    {
        AssertHasNoReferences(
            typeof(DomainAnchor).Assembly,
            "SolidarityGrid.Application",
            "SolidarityGrid.Contracts",
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Node",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions",
            "Grpc");
    }

    [Fact]
    public void ApplicationHasNoForbiddenReferences()
    {
        AssertHasNoReferences(
            typeof(ApplicationAnchor).Assembly,
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Node",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Grpc");
    }

    [Fact]
    public void ContractsHasNoForbiddenReferences()
    {
        AssertHasNoReferences(
            typeof(ContractsAnchor).Assembly,
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Node");
    }

    [Fact]
    public void InfrastructureHasNoNodeReference()
    {
        AssertHasNoReferences(typeof(InfrastructureAnchor).Assembly, "SolidarityGrid.Node");
    }

    [Fact]
    public void DomainDoesNotUseSystemClockOrGeneratePaymentIds()
    {
        var domainPath = Path.Combine(FindRepositoryRoot(), "src", "SolidarityGrid.Domain");
        var source = ReadSourceFiles(domainPath);

        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DomainHasNoPackageReferences()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Domain",
            "SolidarityGrid.Domain.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.DoesNotContain("<PackageReference", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationContainsNoPersistenceImplementation()
    {
        var applicationPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Application");
        var source = ReadSourceFiles(applicationPath);
        string[] forbiddenTerms =
        [
            "DbContext",
            "EntityFramework",
            "Sqlite",
            "SqlConnection",
            "ConnectionString",
            "DbUpdateException",
            "SqliteErrorCode",
        ];

        Assert.All(
            forbiddenTerms,
            term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));
    }

    [Fact]
    public void NodeContainsNoPersistenceImplementation()
    {
        var nodePath = Path.Combine(FindRepositoryRoot(), "src", "SolidarityGrid.Node");
        var source = ReadSourceFiles(nodePath);

        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SolidarityGridUnitOfWork", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InfrastructureReferencesOnlyTheConfiguredEfCoreProvider()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Infrastructure.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains("Microsoft.EntityFrameworkCore.Sqlite", project, StringComparison.Ordinal);
        Assert.Contains("Microsoft.EntityFrameworkCore.Design", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlServer", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionCodeHasNoForbiddenPersistencePatterns()
    {
        var sourcePath = Path.Combine(FindRepositoryRoot(), "src");
        var source = ReadSourceFiles(sourcePath);
        string[] forbiddenTerms =
        [
            "GenericRepository",
            "EnsureCreated",
            "EnsureDeleted",
            "BuildServiceProvider",
            "DateTime.Now",
            "DateTimeOffset.Now",
        ];

        Assert.All(
            forbiddenTerms,
            term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));
    }

    [Fact]
    public void SolutionContainsNoBrokerOrCentralDatabasePackage()
    {
        var root = FindRepositoryRoot();
        var packageFiles = Directory
            .EnumerateFiles(root, "*.props", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(root, "src"),
                "*.csproj",
                SearchOption.AllDirectories));
        var packageConfiguration = string.Join(
            Environment.NewLine,
            packageFiles.Select(File.ReadAllText));
        string[] forbiddenPackages =
        [
            "RabbitMQ",
            "Kafka",
            "MassTransit",
            "Npgsql",
            "SqlServer",
            "Redis",
        ];

        Assert.All(
            forbiddenPackages,
            package => Assert.DoesNotContain(
                package,
                packageConfiguration,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplicationContainsNoHttpOrStaticRuntimeSources()
    {
        var applicationPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Application");
        var source = ReadSourceFiles(applicationPath);
        string[] forbiddenTerms =
        [
            "HttpContext",
            "IResult",
            "ProblemDetails",
            "StatusCodes",
            "Microsoft.AspNetCore",
            "Guid.NewGuid",
            "DateTime.Now",
            "DateTime.UtcNow",
            "DateTimeOffset.Now",
            "DateTimeOffset.UtcNow",
        ];

        Assert.All(
            forbiddenTerms,
            term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));
    }

    [Fact]
    public void PaymentEndpointsDependOnlyOnApplicationBoundary()
    {
        var endpointsPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Node",
            "Payments",
            "PaymentEndpoints.cs");
        var source = File.ReadAllText(endpointsPath);
        string[] forbiddenTerms =
        [
            "SolidarityGridDbContext",
            "PaymentRepository",
            "SqliteConnection",
            "EntityFrameworkCore",
            "RequestServices",
        ];

        Assert.All(
            forbiddenTerms,
            term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));
    }

    [Fact]
    public void InfrastructureContainsNoHttpEndpoint()
    {
        var infrastructurePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Infrastructure");
        var source = ReadSourceFiles(infrastructurePath);

        Assert.DoesNotContain("MapPost", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/pay\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramContainsNoPaymentIdempotencyLogic()
    {
        var programPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Node",
            "Program.cs");
        var source = File.ReadAllText(programPath);

        Assert.DoesNotContain("IdempotencyKey", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentIdempotencyPolicy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitPaymentUseCase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestServices", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionContainsNoServiceLocator()
    {
        var source = ReadSourceFiles(Path.Combine(FindRepositoryRoot(), "src"));

        Assert.DoesNotContain("RequestServices", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationContainsNoGrpcOrTransportImplementation()
    {
        var applicationPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Application");
        var source = ReadSourceFiles(applicationPath);
        string[] forbiddenTerms =
        [
            "GrpcChannel",
            "RpcException",
            "ServerCallContext",
            "Google.Protobuf",
            "HttpProtocols",
            "NodeOptions",
            "SocketsHttpHandler",
        ];

        Assert.All(
            forbiddenTerms,
            term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));
    }

    [Fact]
    public void MeshProtoDefinesOnlyApprovedPaymentCoordinationRpcs()
    {
        var protoPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Contracts",
            "Protos",
            "mesh_control.proto");
        var proto = File.ReadAllText(protoPath);
        var rpcLines = proto
            .Split('\n')
            .Where(line => line.TrimStart().StartsWith("rpc ", StringComparison.Ordinal))
            .ToArray();

        string[] expectedRpcNames =
        [
            "Probe",
            "ReplicatePayment",
            "TryClaimPayment",
            "StartPaymentProcessing",
            "RenewPaymentLease",
            "CompletePayment",
        ];

        Assert.Equal(expectedRpcNames.Length, rpcLines.Length);
        Assert.All(expectedRpcNames, rpcName =>
            Assert.Contains(rpcLines, line =>
                line.Contains($"rpc {rpcName}(", StringComparison.Ordinal)));
    }

    [Fact]
    public void GrpcServerAndClientStayInTheirOwnedLayers()
    {
        var root = FindRepositoryRoot();
        Assert.True(File.Exists(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Node",
            "Mesh",
            "MeshControlGrpcService.cs")));
        Assert.True(File.Exists(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Infrastructure",
            "Mesh",
            "GrpcMeshPeerProbe.cs")));

        var infrastructureProject = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Infrastructure.csproj"));
        Assert.DoesNotContain(
            "SolidarityGrid.Node",
            infrastructureProject,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DockerPublishesOnlyPublicPortAndUsesInternalMeshPort()
    {
        var root = FindRepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));

        Assert.Contains("\"5101:8080\"", compose, StringComparison.Ordinal);
        Assert.Contains("http://node-b:8081", compose, StringComparison.Ordinal);
        Assert.DoesNotContain(":8081:8081", compose, StringComparison.Ordinal);
        Assert.Contains("EXPOSE 8080", dockerfile, StringComparison.Ordinal);
        Assert.Contains("EXPOSE 8081", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void KestrelSeparatesPublicAndMeshProtocols()
    {
        var program = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Node",
            "Program.cs"));

        Assert.Contains("ListenAnyIP(8080", program, StringComparison.Ordinal);
        Assert.Contains("HttpProtocols.Http1", program, StringComparison.Ordinal);
        Assert.Contains("ListenAnyIP(8081", program, StringComparison.Ordinal);
        Assert.Contains("HttpProtocols.Http2", program, StringComparison.Ordinal);
        Assert.Contains("RequireHost(\"*:8081\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeDoesNotOwnGrpcClientOrPaymentMeshLogic()
    {
        var nodePath = Path.Combine(FindRepositoryRoot(), "src", "SolidarityGrid.Node");
        var nodeSource = ReadSourceFiles(nodePath);
        var serviceSource = File.ReadAllText(Path.Combine(
            nodePath,
            "Mesh",
            "MeshControlGrpcService.cs"));

        Assert.DoesNotContain("GrpcChannel", nodeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MeshControlClient", nodeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRepository", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitPayment", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PaymentReplicationCoordinator",
            serviceSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MeshClientHasReplicationWithoutRetries()
    {
        var meshPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Infrastructure",
            "Mesh");
        var source = ReadSourceFiles(meshPath);

        Assert.DoesNotContain("Polly", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Retry", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GrpcPaymentReplicaTransport", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MeshChannelPoolHasNoStaticChannelState()
    {
        var fields = typeof(SolidarityGrid.Infrastructure.Mesh.GrpcMeshChannelPool)
            .GetFields(BindingFlags.Instance | BindingFlags.Static |
                       BindingFlags.NonPublic | BindingFlags.Public);

        Assert.DoesNotContain(
            fields,
            field => field.IsStatic &&
                     field.FieldType.FullName?.Contains(
                         "GrpcChannel",
                         StringComparison.Ordinal) == true);
    }

    [Fact]
    public void FailureDetectorRespectsLayerOwnership()
    {
        var root = FindRepositoryRoot();
        var application = ReadSourceFiles(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Application"));
        var infrastructure = ReadSourceFiles(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Infrastructure"));
        var node = ReadSourceFiles(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Node"));

        Assert.DoesNotContain("BackgroundService", application, StringComparison.Ordinal);
        Assert.DoesNotContain("IHostedService", application, StringComparison.Ordinal);
        Assert.DoesNotContain("GrpcChannel", application, StringComparison.Ordinal);
        Assert.Contains(
            "MeshHeartbeatBackgroundService",
            infrastructure,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MeshPeerHealthMonitor", node, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", node, StringComparison.Ordinal);

        var heartbeatService = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Infrastructure",
            "Mesh",
            "MeshHeartbeatBackgroundService.cs"));
        Assert.DoesNotContain("DbContext", heartbeatService, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IServiceScopeFactory",
            heartbeatService,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateScope", heartbeatService, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", heartbeatService, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "System.Threading.Timer",
            heartbeatService,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PeerHealthIsNotPersisted()
    {
        var root = FindRepositoryRoot();
        var persistence = ReadSourceFiles(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Infrastructure",
            "Persistence"));

        Assert.DoesNotContain("MeshPeerHealth", persistence, StringComparison.Ordinal);
        Assert.DoesNotContain("PeerHealth", persistence, StringComparison.Ordinal);
        Assert.DoesNotContain("Heartbeat", persistence, StringComparison.Ordinal);
    }

    [Fact]
    public void MeshContractContainsNoUnapprovedRpcOrTakeover()
    {
        var root = FindRepositoryRoot();
        var proto = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Contracts",
            "Protos",
            "mesh_control.proto"));
        Assert.DoesNotContain("rpc Heartbeat", proto, StringComparison.Ordinal);
        Assert.DoesNotContain("rpc Gossip", proto, StringComparison.Ordinal);
        Assert.DoesNotContain("rpc Vote", proto, StringComparison.Ordinal);
        Assert.Equal(
            6,
            proto.Split('\n').Count(
                line => line.TrimStart().StartsWith("rpc ", StringComparison.Ordinal)));
        Assert.DoesNotContain("rpc Takeover", proto, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentProcessingAddsNoSchemaOrForbiddenCoordinator()
    {
        var root = FindRepositoryRoot();
        var migrations = Directory.GetFiles(
            Path.Combine(
                root,
                "src",
                "SolidarityGrid.Infrastructure",
                "Persistence",
                "Migrations"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var nonDomainSource = string.Join(
            Environment.NewLine,
            ReadSourceFiles(Path.Combine(root, "src", "SolidarityGrid.Application")),
            ReadSourceFiles(Path.Combine(root, "src", "SolidarityGrid.Infrastructure")),
            ReadSourceFiles(Path.Combine(root, "src", "SolidarityGrid.Node")));

        Assert.Equal(3, migrations.Length);
        Assert.DoesNotContain("ConsensusEngine", nonDomainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TakeoverWorker", nonDomainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildServiceProvider", nonDomainSource, StringComparison.Ordinal);
        Assert.Contains(
            "PaymentProcessingBackgroundService",
            nonDomainSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "GrpcPaymentCoordinationTransport",
            nonDomainSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentProcessingRespectsLayerAndScopeBoundaries()
    {
        var root = FindRepositoryRoot();
        var application = ReadSourceFiles(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Application"));
        var infrastructure = ReadSourceFiles(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Infrastructure"));
        var node = ReadSourceFiles(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Node"));
        var migration = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Infrastructure",
            "Persistence",
            "Migrations",
            "20260723205054_InitialNodePaymentStorage.cs"));

        Assert.DoesNotContain("Grpc.", application, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", application, StringComparison.Ordinal);
        Assert.Contains(
            "GrpcPaymentCoordinationTransport",
            infrastructure,
            StringComparison.Ordinal);
        Assert.Contains(
            "PaymentProcessingBackgroundService",
            infrastructure,
            StringComparison.Ordinal);
        Assert.Contains(
            "MeshControlGrpcService",
            node,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            migration.Split("CreateTable(", StringSplitOptions.None).Length - 1);
        Assert.Equal(
            5,
            migration.Split("CreateIndex(", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("GlobalLeader", application, StringComparison.Ordinal);
        Assert.DoesNotContain("VoteRepository", infrastructure, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoricalReconciliation", infrastructure, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildServiceProvider", infrastructure, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureDetectorUsesNoStaticTimerOrRegistry()
    {
        var registryFields = typeof(
                SolidarityGrid.Application.Mesh.Health.InMemoryMeshPeerHealthRegistry)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var serviceFields = typeof(
                SolidarityGrid.Infrastructure.Mesh.MeshHeartbeatBackgroundService)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.Empty(registryFields);
        Assert.DoesNotContain(
            serviceFields,
            field => field.FieldType == typeof(System.Threading.Timer));
    }

    [Fact]
    public void AutomaticTakeoverReusesExistingBoundariesAndProtocol()
    {
        var root = FindRepositoryRoot();
        var domain = ReadSourceFiles(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Domain"));
        var application = ReadSourceFiles(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Application"));
        var production = string.Join(
            Environment.NewLine,
            application,
            ReadSourceFiles(Path.Combine(
                root,
                "src",
                "SolidarityGrid.Infrastructure")),
            ReadSourceFiles(Path.Combine(
                root,
                "src",
                "SolidarityGrid.Node")));
        var paymentBackgroundServices = Directory.GetFiles(
            Path.Combine(
                root,
                "src",
                "SolidarityGrid.Infrastructure",
                "Payments"),
            "*BackgroundService.cs",
            SearchOption.TopDirectoryOnly);
        var migrations = Directory.GetFiles(
            Path.Combine(
                root,
                "src",
                "SolidarityGrid.Infrastructure",
                "Persistence",
                "Migrations"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var proto = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Contracts",
            "Protos",
            "mesh_control.proto"));
        var paymentEndpoints = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SolidarityGrid.Node",
            "Payments",
            "PaymentEndpoints.cs"));

        Assert.Single(paymentBackgroundServices);
        Assert.EndsWith(
            "PaymentProcessingBackgroundService.cs",
            paymentBackgroundServices[0],
            StringComparison.Ordinal);
        Assert.DoesNotContain("MeshPeerHealth", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("Grpc.", application, StringComparison.Ordinal);
        Assert.Equal(
            6,
            proto.Split('\n').Count(line =>
                line.TrimStart().StartsWith("rpc ", StringComparison.Ordinal)));
        Assert.Equal(3, migrations.Length);

        string[] forbiddenTerms =
        [
            "RecoveryBackgroundService",
            "TakeoverBackgroundService",
            "ConsensusEngine",
            "GlobalLeader",
            "VoteRepository",
            "Redis",
            "RabbitMQ",
            "MassTransit",
            "Kafka",
            "BuildServiceProvider",
            "RequestServices",
        ];
        Assert.All(forbiddenTerms, term =>
            Assert.DoesNotContain(
                term,
                production,
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            "takeover",
            paymentEndpoints,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertHasNoReferences(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var references = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            references,
            reference => forbiddenPrefixes.Any(
                prefix => reference.Equals(prefix, StringComparison.Ordinal) ||
                          reference.StartsWith($"{prefix}.", StringComparison.Ordinal)));
    }

    private static string ReadSourceFiles(string directory) =>
        string.Join(
            Environment.NewLine,
            Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SolidarityGrid.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
