using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using LinqToDB.SqlQuery;

namespace GtG.Open.HC.SelectionAwarePaging.Tests;

public sealed class SqlTrace
{
    private readonly ConcurrentQueue<string> _commands = new();

    public IReadOnlyList<string> Commands => _commands.ToArray();

    public void Clear() => _commands.Clear();

    public void Capture(TraceInfo info)
    {
        if (info.TraceInfoStep == TraceInfoStep.BeforeExecute && info.SqlText is { } sql)
        {
            _commands.Enqueue(sql.Trim());
        }
    }
}
