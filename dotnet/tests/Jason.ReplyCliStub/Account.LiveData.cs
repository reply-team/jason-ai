using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// Live Data and the contact lists a search fills, as reply-cli 0.20.0 documents them: <c>contact-list create|get|delete|
/// contacts</c> and <c>live-data preview|start|get|list|delete|filter-values</c>. A search is a record in
/// <c>searches.json</c> that a test moves and gives people to, as Reply's search service would; a person it finds is a
/// contact Live Data created, on the lists the search fills. A preview answers what <c>preview.json</c> holds, and a
/// filter's values are those <c>filter-values.json</c> holds for its kind.
/// </summary>
internal static partial class Account
{
    private const string SearchesFile = "searches.json";
    private const string PreviewFile = "preview.json";
    private const string FilterValuesFile = "filter-values.json";

    /// <summary>Where the identifiers of the lists and searches this stand-in makes start: clear of every planted one.</summary>
    private const int FirstCreatedListId = 3001;

    private const int FirstCreatedSearchId = 4001;

    /// <summary>The background job every preview answers with, a GUID as Reply issues one.</summary>
    private const string PreviewJob = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private const int CollectionPageCap = 1000;

    private const int LargestOffset = 1_000_000;

    /// <summary>The statuses of a search that has not ended, which block a start of the same filters.</summary>
    private static readonly string[] Unfinished = ["inProgress", "manuallyPaused", "pausedDueToInsufficientCredits", "pausedDueToContactLimit"];

    private static readonly string[] FilterValueKinds = ["industries", "departments", "seniorities", "locations", "job-titles"];

    /// <summary>The members of Reply's v3 contact model, the only ones a list's read answers.</summary>
    private static readonly string[] ContactModel =
    [
        "id", "email", "domain", "firstName", "lastName", "phone", "title", "company", "companySize", "industry", "city", "state",
        "country", "linkedInUrl", "linkedInSalesNavigatorUrl", "accountId", "stage", "addingDate", "createdAt", "customFields",
    ];

    /// <summary><c>contact-list create --name -</c>: an empty list of a name the account does not hold yet, private unless <c>--shared</c>.</summary>
    private static Ending ListCreate(string root, Parsed line, string? stdin)
    {
        var (name, refusal) = NameOf(line, stdin, "create", "contact-list");
        if (refusal is not null)
        {
            return refusal;
        }

        var lists = Read(root, ListsFile);
        if (lists.OfType<JsonObject>().Any(list => string.Equals(Text(list, "name"), name, StringComparison.OrdinalIgnoreCase)))
        {
            return Refused("contact-list.name_taken", "This account already has a contact list of that name.", status: 409, replyCode: "contactsList.duplicateName");
        }

        var id = Math.Max(FirstCreatedListId, lists.OfType<JsonObject>().Select(list => list["id"]!.GetValue<int>() + 1).DefaultIfEmpty(0).Max());
        var shared = line.Has("--shared");
        lists.Add(new JsonObject { ["id"] = id, ["name"] = name, ["isShared"] = shared, ["members"] = new JsonArray() });
        Write(root, ListsFile, lists);
        return Done(new JsonObject { ["status"] = "created", ["list_id"] = id, ["name"] = name, ["is_shared"] = shared });
    }

    private static Ending ListGet(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("List id");
        }

        return Find(Read(root, ListsFile), id) is { } list
            ? Done(new JsonObject { ["id"] = id, ["name"] = list["name"]!.DeepClone(), ["isShared"] = Flag(list, "isShared") })
            : NoList(id);
    }

    /// <summary><c>contact-list delete</c>: the list goes and the contacts on it stay in the account.</summary>
    private static Ending ListDelete(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("List id");
        }

        var lists = Read(root, ListsFile);
        if (Find(lists, id) is not { } list)
        {
            return NoList(id, new JsonObject { ["list_id"] = id });
        }

        lists.Remove(list);
        Write(root, ListsFile, lists);
        return Done(new JsonObject { ["status"] = "deleted", ["list_id"] = id });
    }

    /// <summary>
    /// <c>contact-list contacts</c>: one page of a list's people, oldest first by the date each was added and, for equal
    /// dates, by the highest id; with <c>--live-data-only</c> only those Live Data created. Reply does not check the list
    /// here, so a list it does not hold reads as empty.
    /// </summary>
    private static Ending ListPeople(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("List id");
        }

        var (limit, badLimit) = RangeOf(line, "--limit", 1, CollectionPageCap, 100);
        var (offset, badOffset) = RangeOf(line, "--offset", 0, LargestOffset, 0);
        if ((badLimit ?? badOffset) is { } bad)
        {
            return bad;
        }

        var members = Find(Read(root, ListsFile), id)?["members"] as JsonArray ?? [];
        var held = members.Select(member => member!.GetValue<int>()).ToHashSet();
        var people = Records(root, ContactsFile)
            .Where(contact => held.Contains(contact["id"]!.GetValue<int>()))
            .Where(contact => !line.Has("--live-data-only") || Text(contact, "creationSource") == "liveData")
            .OrderBy(contact => DateTimeOffset.TryParse(Text(contact, "addingDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : DateTimeOffset.MinValue)
            .ThenByDescending(contact => contact["id"]!.GetValue<int>())
            .ToList();

        var items = new JsonArray([.. people.Skip(offset).Take(limit).Select(contact => (JsonNode)AsReplyContact(contact))]);
        return Done(new JsonObject { ["items"] = items, ["has_more"] = people.Count > offset + limit });
    }

    /// <summary>
    /// <c>live-data preview</c>: the counts and the sample Reply's background job answers, or that the job is still running
    /// when the wait ran out. Nothing is saved or charged.
    /// </summary>
    private static Ending LiveDataPreview(string root, Parsed line, string? stdin)
    {
        var job = line.Option("--job-id");
        if ((line.Option("--filters") is null) == (job is null))
        {
            return Refused("usage.filters", "Give exactly one of --filters and --job-id.", exit: 2);
        }

        if (job is not null && line.Option("--max-per-company") is not null)
        {
            return Refused("usage.filters", "--max-per-company goes with --filters: a preview already started keeps its own.", exit: 2);
        }

        var (_, badWait) = RangeOf(line, "--wait-seconds", 0, 600, 120);
        var (_, badPerCompany) = RangeOf(line, "--max-per-company", 1, int.MaxValue, 1);
        if ((badWait ?? badPerCompany) is { } bad)
        {
            return bad;
        }

        if (job is null)
        {
            var (filters, refusal) = FiltersOf(line, stdin);
            if (refusal is not null)
            {
                return refusal;
            }

            if (filters!.Count == 0)
            {
                return NoFilter();
            }
        }

        var planted = File.Exists(Path.Combine(root, PreviewFile)) ? JsonNode.Parse(File.ReadAllText(Path.Combine(root, PreviewFile))) as JsonObject : null;
        if (planted is not null && Text(planted, "kind") == "pending")
        {
            return Done(new JsonObject { ["status"] = "pending", ["job_id"] = PreviewJob, ["progress"] = null });
        }

        return Done(new JsonObject
        {
            ["status"] = "done",
            ["job_id"] = PreviewJob,
            ["contacts_count"] = planted?["contacts_count"]?.DeepClone() ?? 0,
            ["companies_count"] = planted?["companies_count"]?.DeepClone() ?? 0,
            ["sample_contacts"] = planted?["sample_contacts"]?.DeepClone() ?? new JsonArray(),
        });
    }

    /// <summary>
    /// <c>live-data start</c>: a search on the lists named, which Reply refuses while an unfinished search of the team has
    /// the same filters, without the credits for one contact, without the user's contact permissions, without a filter,
    /// or with a list the account lacks — starting nothing.
    /// </summary>
    private static Ending LiveDataStart(string root, Parsed line, string? stdin)
    {
        var (filters, refusal) = FiltersOf(line, stdin);
        if (refusal is not null)
        {
            return refusal;
        }

        if (line.Option("--count") is null)
        {
            return Refused("usage.range", "No --count was given.", exit: 2);
        }

        var (count, badCount) = RangeOf(line, "--count", 1, int.MaxValue, 1);
        var (perCompany, badPerCompany) = RangeOf(line, "--max-per-company", 1, int.MaxValue, 1);
        if ((badCount ?? badPerCompany) is { } bad)
        {
            return bad;
        }

        var listIds = new List<int>();
        if (line.Option("--list-id") is { } listed)
        {
            if (IdOf(listed) is not { } listId)
            {
                return BadId("--list-id");
            }

            listIds.Add(listId);
        }

        var lists = Read(root, ListsFile);
        if (listIds.Any(listId => Find(lists, listId) is null))
        {
            return Refused("contact-list.not_found", "A list given with --list-id does not exist or is not accessible.", status: 404);
        }

        if (!Enabled(root, "liveData"))
        {
            return Refused("access.feature_unavailable", "The account's plan or role does not include this.", status: 403, replyCode: "liveDataSearch.forbidden",
                detail: "You do not have permission to add contacts.");
        }

        if (filters!.Count == 0)
        {
            return NoFilter();
        }

        var searches = Read(root, SearchesFile);
        if (searches.OfType<JsonObject>().Any(search => Unfinished.Contains(Text(search, "status")) && JsonNode.DeepEquals(search["filters"], filters)))
        {
            return Refused("live_data.search_in_progress", "An unfinished search of this team with the same filters blocks the start.", status: 409,
                replyCode: "liveDataSearch.searchInProgress", detail: "The same search is already in progress for the team.");
        }

        if (!Enabled(root, "liveDataCredits"))
        {
            return Refused("live_data.insufficient_credits", "The team does not have the credits for a single contact.", status: 422,
                replyCode: "liveDataSearch.insufficientCredits", detail: "Not enough credits to start the search.");
        }

        var id = Math.Max(FirstCreatedSearchId, searches.OfType<JsonObject>().Select(search => search["id"]!.GetValue<int>() + 1).DefaultIfEmpty(0).Max());
        searches.Add(new JsonObject
        {
            ["id"] = id,
            ["status"] = "inProgress",
            ["startedAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ff", CultureInfo.InvariantCulture),
            ["contactsFound"] = 0,
            ["usedCredits"] = 0,
            ["userId"] = 501,
            ["sequenceId"] = null,
            ["sequenceName"] = null,
            ["lists"] = new JsonArray([.. listIds.Select(listId => (JsonNode)new JsonObject { ["id"] = listId, ["name"] = Find(lists, listId)!["name"]!.DeepClone() })]),
            ["filters"] = filters.DeepClone(),
            ["_count"] = count,
            ["_maxPerCompany"] = perCompany,
            ["_withPhones"] = line.Has("--with-phones"),
        });
        Write(root, SearchesFile, searches);
        return Done(new JsonObject { ["status"] = "started", ["search_id"] = id });
    }

    private static Ending LiveDataGet(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Search id");
        }

        return Find(Read(root, SearchesFile), id) is { } search ? Done(Shown(search)) : NoSearch(id);
    }

    /// <summary><c>live-data list</c>: one page of the team's searches, newest first, without their filters.</summary>
    private static Ending LiveDataList(string root, Parsed line)
    {
        var (limit, badLimit) = RangeOf(line, "--limit", 1, CollectionPageCap, 100);
        var (offset, badOffset) = RangeOf(line, "--offset", 0, LargestOffset, 0);
        if ((badLimit ?? badOffset) is { } bad)
        {
            return bad;
        }

        var searches = Records(root, SearchesFile)
            .OrderByDescending(search => DateTimeOffset.TryParse(Text(search, "startedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : DateTimeOffset.MinValue)
            .ThenByDescending(search => search["id"]!.GetValue<int>())
            .ToList();
        var items = new JsonArray();
        foreach (var search in searches.Skip(offset).Take(limit))
        {
            var listed = Shown(search);
            listed.Remove("filters");
            items.Add(listed);
        }

        return Done(new JsonObject { ["items"] = items, ["has_more"] = searches.Count > offset + limit });
    }

    /// <summary><c>live-data delete</c>: the search goes, whatever its status; the contacts it added stay.</summary>
    private static Ending LiveDataDelete(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Search id");
        }

        var searches = Read(root, SearchesFile);
        if (Find(searches, id) is not { } search)
        {
            return NoSearch(id);
        }

        searches.Remove(search);
        Write(root, SearchesFile, searches);
        return Done(new JsonObject { ["status"] = "deleted", ["search_id"] = id });
    }

    /// <summary><c>live-data filter-values</c>: one page of the values Reply suggests for a kind, those containing the text ignoring case.</summary>
    private static Ending LiveDataFilterValues(string root, Parsed line)
    {
        var kind = line.Operands[0];
        if (!FilterValueKinds.Contains(kind))
        {
            return Refused("usage.choice", $"The kind must be one of: {string.Join(", ", FilterValueKinds)}.", exit: 2);
        }

        var (limit, badLimit) = RangeOf(line, "--limit", 1, CollectionPageCap, 100);
        var (offset, badOffset) = RangeOf(line, "--offset", 0, LargestOffset, 0);
        if ((badLimit ?? badOffset) is { } bad)
        {
            return bad;
        }

        var planted = File.Exists(Path.Combine(root, FilterValuesFile)) ? JsonNode.Parse(File.ReadAllText(Path.Combine(root, FilterValuesFile))) as JsonObject : null;
        var search = line.Option("--search") ?? string.Empty;
        var values = (planted?[kind] as JsonArray ?? [])
            .Select(value => value!.GetValue<string>())
            .Where(value => value.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var items = new JsonArray([.. values.Skip(offset).Take(limit).Select(value => (JsonNode)JsonValue.Create(value))]);
        return Done(new JsonObject { ["items"] = items, ["has_more"] = values.Count > offset + limit });
    }

    /// <summary>
    /// The filters as this package passes them, <c>--filters -</c> and the object on stdin: inline JSON and a file are
    /// forms of the real CLI's that the package never uses, so they are refused here rather than read.
    /// </summary>
    private static (JsonObject? Filters, Ending? Refusal) FiltersOf(Parsed line, string? stdin)
    {
        if (line.Option("--filters") != "-")
        {
            return (null, Refused("usage.filters", "The stand-in reads --filters from stdin only: the package passes -.", exit: 2));
        }

        try
        {
            return JsonNode.Parse(stdin ?? string.Empty) is JsonObject filters
                ? (filters, null)
                : (null, Refused("usage.filters", "--filters must be a JSON object in Reply's filter shape.", exit: 2));
        }
        catch (JsonException)
        {
            return (null, Refused("usage.filters", "--filters must be a JSON object in Reply's filter shape.", exit: 2));
        }
    }

    /// <summary>A contact as Reply's v3 model answers it, without what this stand-in keeps for itself.</summary>
    private static JsonObject AsReplyContact(JsonObject contact)
    {
        var shown = new JsonObject();
        foreach (var name in ContactModel)
        {
            if (contact.TryGetPropertyValue(name, out var value))
            {
                shown[name] = value?.DeepClone();
            }
        }

        return shown;
    }

    private static Ending NoFilter() =>
        Refused("live_data.invalid_filters", "Reply did not accept the request, most often the filters; its detail names what.", status: 400,
            replyCode: "liveDataSearch.invalidParameter", detail: "At least one filter must be provided.");

    private static Ending NoList(int id, JsonObject? ids = null) =>
        Refused("contact-list.not_found", $"This account has no contact list {id}.", status: 404, replyCode: "contactsList.notFound", ids: ids);

    private static Ending NoSearch(int id) =>
        Refused("live_data.not_found", "This team has no such Live Data search.", status: 404, replyCode: "liveDataSearch.notFound",
            ids: new JsonObject { ["search_id"] = id });
}
