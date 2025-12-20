using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ANcpLua.Interceptors.ErrorOr.Generator.Helpers;
using ANcpLua.Interceptors.ErrorOr.Generator.Interceptors;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorOr.Interceptors.Benchmarks;

[MemoryDiagnoser]
public class InterceptorBenchmarks
{
    private SyntaxNode[] _invocationNodes = Array.Empty<SyntaxNode>();
    private ImmutableArray<MapCallSite> _callSites = ImmutableArray<MapCallSite>.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _invocationNodes = BuildInvocationNodes();
        _callSites = BuildCallSites(128);
    }

    [Benchmark]
    public int IsMapInvocation_Throughput()
    {
        var count = 0;
        var nodes = _invocationNodes;
        for (var i = 0; i < nodes.Length; i++)
        {
            if (ErrorOrInterceptorGenerator.IsMapInvocation(nodes[i]))
                count++;
        }

        return count;
    }

    [Benchmark]
    public int GroupCallSites_100Plus()
    {
        var grouped = ErrorOrInterceptorGenerator.GroupCallSites(_callSites);
        return grouped.Length;
    }

    [Benchmark]
    public int CodeBuilder_Allocation()
    {
        var builder = new CodeBuilder();
        for (var i = 0; i < 512; i++)
        {
            builder.AppendLine("line");
            builder.AppendLine($"line {i}");
        }

        return builder.Length;
    }

    private static SyntaxNode[] BuildInvocationNodes()
    {
        const string source = """
                              using Microsoft.AspNetCore.Builder;
                              var app = WebApplication.CreateBuilder().Build();
                              app.MapGet("/a", () => "ok");
                              app.MapPost("/b", () => "ok");
                              app.MapPut("/c", () => "ok");
                              app.MapDelete("/d", () => "ok");
                              app.MapPatch("/e", () => "ok");
                              app.MapSomethingElse("/f", () => "ok");
                              """;

        var tree = CSharpSyntaxTree.ParseText(source, path: "Program.cs");
        var root = tree.GetRoot();
        List<SyntaxNode> nodes = [];

        foreach (var node in root.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax)
                nodes.Add(node);
        }

        return nodes.ToArray();
    }

    private static ImmutableArray<MapCallSite> BuildCallSites(int count)
    {
        var notFound = new EquatableArray<int>(ImmutableArray.Create(4));
        var validationNotFound = new EquatableArray<int>(ImmutableArray.Create(2, 4));
        List<MapCallSite> sites = new(count);

        for (var i = 0; i < count; i++)
        {
            var method = (i & 1) == 0 ? "Get" : "Post";
            var successType = (i % 3) == 0 ? "global::User" : "global::Order";
            var errors = (i % 2) == 0 ? notFound : validationNotFound;
            var location = $"[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, \"{i}:1\")]";

            sites.Add(new MapCallSite(method, successType, false, errors, location));
        }

        return sites.ToImmutableArray();
    }
}
