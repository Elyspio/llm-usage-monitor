using System.Xml.Linq;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB;

/// <summary>
///     Stores the ASP.NET Data Protection key ring in MongoDB.
/// </summary>
internal sealed class MongoXmlRepository(IMongoDatabase database) : IXmlRepository
{
	private readonly IMongoCollection<DataProtectionKeyDocument> _keys = database.GetCollection<DataProtectionKeyDocument>(Collections.DataProtectionKeys);

	public IReadOnlyCollection<XElement> GetAllElements()
	{
		return _keys.Find(FilterDefinition<DataProtectionKeyDocument>.Empty)
			.ToList()
			.Select(key => XElement.Parse(key.Xml))
			.ToList();
	}

	public void StoreElement(XElement element, string friendlyName)
	{
		_keys.InsertOne(new()
		{
			FriendlyName = friendlyName,
			Xml = element.ToString(SaveOptions.DisableFormatting)
		});
	}
}

/// <summary>
///     A key ring kept in memory, for the build-time OpenAPI generation: it never reaches MongoDB nor writes keys on the disk.
/// </summary>
internal sealed class TransientXmlRepository : IXmlRepository
{
	private readonly List<XElement> _elements = [];

	public IReadOnlyCollection<XElement> GetAllElements()
	{
		lock (_elements)
		{
			return _elements.Select(element => new XElement(element)).ToList();
		}
	}

	public void StoreElement(XElement element, string friendlyName)
	{
		lock (_elements)
		{
			_elements.Add(new(element));
		}
	}
}

/// <summary>
///     Encrypts the secrets stored with the settings (the ntfy token) with the key ring of <see cref="MongoXmlRepository" />.
/// </summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
	private readonly IDataProtector _protector = provider.CreateProtector("llm-usage-monitor.settings.secrets");

	public string Protect(string value)
	{
		return _protector.Protect(value);
	}

	public string Unprotect(string value)
	{
		return _protector.Unprotect(value);
	}
}
