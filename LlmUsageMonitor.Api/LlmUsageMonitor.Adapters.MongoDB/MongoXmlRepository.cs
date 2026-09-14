using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB;

/// <summary>
///     Stores the ASP.NET Data Protection key ring in MongoDB.
/// </summary>
internal sealed class MongoXmlRepository(IMongoDatabase database) : IXmlRepository
{
	private readonly IMongoCollection<DataProtectionKeyDocument> _keys = database.GetCollection<DataProtectionKeyDocument>(Collections.DataProtectionKeys);

	public IReadOnlyCollection<XElement> GetAllElements() => _keys.Find(FilterDefinition<DataProtectionKeyDocument>.Empty)
		.ToList()
		.Select(key => XElement.Parse(key.Xml))
		.ToList();

	public void StoreElement(XElement element, string friendlyName) => _keys.InsertOne(new DataProtectionKeyDocument
	{
		FriendlyName = friendlyName,
		Xml = element.ToString(SaveOptions.DisableFormatting),
	});
}
