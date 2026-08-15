using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

	// {{ AOT assemblies
	public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
	{
		"System.Text.Json.dll",
		"mscorlib.dll",
	};
	// }}

	// {{ constraint implement type
	// }} 

	// {{ AOT generic types
	// System.Action<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Action<object,int>
	// System.Action<object,object>
	// System.Action<object>
	// System.Buffers.ArrayPool<System.ValueTuple<object,System.Text.Json.JsonReaderState,long,object,object>>
	// System.Buffers.ArrayPool<System.ValueTuple<object,object,object>>
	// System.Buffers.ArrayPool<byte>
	// System.Buffers.ArrayPool<object>
	// System.Buffers.MemoryManager<byte>
	// System.Buffers.ReadOnlySequence.<>c<byte>
	// System.Buffers.ReadOnlySequence<byte>
	// System.Buffers.ReadOnlySequenceSegment<byte>
	// System.Buffers.SpanAction<ushort,System.Buffers.ReadOnlySequence<ushort>>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool.LockedStack<System.ValueTuple<object,System.Text.Json.JsonReaderState,long,object,object>>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool.LockedStack<System.ValueTuple<object,object,object>>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool.LockedStack<byte>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool.LockedStack<object>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool.PerCoreLockedStacks<System.ValueTuple<object,System.Text.Json.JsonReaderState,long,object,object>>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool.PerCoreLockedStacks<System.ValueTuple<object,object,object>>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool.PerCoreLockedStacks<byte>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool.PerCoreLockedStacks<object>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool<System.ValueTuple<object,System.Text.Json.JsonReaderState,long,object,object>>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool<System.ValueTuple<object,object,object>>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool<byte>
	// System.Buffers.TlsOverPerCoreLockedStacksArrayPool<object>
	// System.ByReference<byte>
	// System.ByReference<ushort>
	// System.Collections.Generic.ArraySortHelper<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.ArraySortHelper<object>
	// System.Collections.Generic.Comparer<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.Comparer<System.Text.Json.JsonReaderState>
	// System.Collections.Generic.Comparer<long>
	// System.Collections.Generic.Comparer<object>
	// System.Collections.Generic.Dictionary.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection<object,object>
	// System.Collections.Generic.Dictionary<object,object>
	// System.Collections.Generic.EqualityComparer<System.Text.Json.JsonReaderState>
	// System.Collections.Generic.EqualityComparer<int>
	// System.Collections.Generic.EqualityComparer<long>
	// System.Collections.Generic.EqualityComparer<object>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.ICollection<object>
	// System.Collections.Generic.IComparer<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IComparer<object>
	// System.Collections.Generic.IDictionary<object,System.Text.Json.JsonElement>
	// System.Collections.Generic.IDictionary<object,object>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerable<object>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerator<object>
	// System.Collections.Generic.IEqualityComparer<object>
	// System.Collections.Generic.IList<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IList<object>
	// System.Collections.Generic.KeyValuePair<object,object>
	// System.Collections.Generic.List.Enumerator<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.List.Enumerator<object>
	// System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.List<object>
	// System.Collections.Generic.ObjectComparer<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.ObjectComparer<System.Text.Json.JsonReaderState>
	// System.Collections.Generic.ObjectComparer<long>
	// System.Collections.Generic.ObjectComparer<object>
	// System.Collections.Generic.ObjectEqualityComparer<System.Text.Json.JsonReaderState>
	// System.Collections.Generic.ObjectEqualityComparer<int>
	// System.Collections.Generic.ObjectEqualityComparer<long>
	// System.Collections.Generic.ObjectEqualityComparer<object>
	// System.Collections.ObjectModel.ReadOnlyCollection<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.ObjectModel.ReadOnlyCollection<object>
	// System.Comparison<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Comparison<object>
	// System.Func<int>
	// System.Func<object,byte>
	// System.Func<object,int,byte>
	// System.Func<object,int>
	// System.Func<object,object,byte>
	// System.Func<object,object>
	// System.Func<object>
	// System.Memory<byte>
	// System.Nullable<byte>
	// System.Nullable<int>
	// System.Predicate<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Predicate<object>
	// System.ReadOnlyMemory<byte>
	// System.ReadOnlySpan<byte>
	// System.ReadOnlySpan<ushort>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>
	// System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder<int>
	// System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder<object>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<int>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<object>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<int>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<object>
	// System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter<int>
	// System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter<object>
	// System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<int>
	// System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<object>
	// System.Runtime.CompilerServices.TaskAwaiter<int>
	// System.Runtime.CompilerServices.TaskAwaiter<object>
	// System.Span<byte>
	// System.Span<ushort>
	// System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter<object,object>
	// System.Text.Json.Serialization.Converters.JsonMetadataServicesConverter<int>
	// System.Text.Json.Serialization.Converters.JsonMetadataServicesConverter<object>
	// System.Text.Json.Serialization.Converters.LargeObjectWithParameterizedConstructorConverter<object>
	// System.Text.Json.Serialization.Converters.ListOfTConverter<object,object>
	// System.Text.Json.Serialization.Converters.ObjectDefaultConverter<object>
	// System.Text.Json.Serialization.Converters.ObjectWithParameterizedConstructorConverter<object>
	// System.Text.Json.Serialization.JsonCollectionConverter<object,object>
	// System.Text.Json.Serialization.JsonConverter<int>
	// System.Text.Json.Serialization.JsonConverter<object>
	// System.Text.Json.Serialization.JsonDictionaryConverter<int>
	// System.Text.Json.Serialization.JsonDictionaryConverter<object>
	// System.Text.Json.Serialization.JsonObjectConverter<object>
	// System.Text.Json.Serialization.JsonResumableConverter<int>
	// System.Text.Json.Serialization.JsonResumableConverter<object>
	// System.Text.Json.Serialization.Metadata.JsonCollectionInfoValues<object>
	// System.Text.Json.Serialization.Metadata.JsonObjectInfoValues<object>
	// System.Text.Json.Serialization.Metadata.JsonParameterInfo<int>
	// System.Text.Json.Serialization.Metadata.JsonParameterInfo<object>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass10_0<int>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass10_0<object>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass10_1<int>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass10_1<object>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass15_0<int>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass15_0<object>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass15_1<int>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass15_1<object>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass9_0<int>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass9_0<object>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass9_1<int>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo.<>c__DisplayClass9_1<object>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo<int>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfo<object>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfoValues<int>
	// System.Text.Json.Serialization.Metadata.JsonPropertyInfoValues<object>
	// System.Text.Json.Serialization.Metadata.JsonTypeInfo.<>c__DisplayClass29_0<int>
	// System.Text.Json.Serialization.Metadata.JsonTypeInfo.<>c__DisplayClass29_0<object>
	// System.Text.Json.Serialization.Metadata.JsonTypeInfo.<>c__DisplayClass29_1<int>
	// System.Text.Json.Serialization.Metadata.JsonTypeInfo.<>c__DisplayClass29_1<object>
	// System.Text.Json.Serialization.Metadata.JsonTypeInfo<int>
	// System.Text.Json.Serialization.Metadata.JsonTypeInfo<object>
	// System.Threading.Tasks.Sources.IValueTaskSource<int>
	// System.Threading.Tasks.Sources.IValueTaskSource<object>
	// System.Threading.Tasks.Task<int>
	// System.Threading.Tasks.Task<object>
	// System.Threading.Tasks.ValueTask.ValueTaskSourceAsTask.<>c<int>
	// System.Threading.Tasks.ValueTask.ValueTaskSourceAsTask.<>c<object>
	// System.Threading.Tasks.ValueTask.ValueTaskSourceAsTask<int>
	// System.Threading.Tasks.ValueTask.ValueTaskSourceAsTask<object>
	// System.Threading.Tasks.ValueTask<int>
	// System.Threading.Tasks.ValueTask<object>
	// System.ValueTuple<object,System.Text.Json.JsonReaderState,long,object,object>
	// System.ValueTuple<object,object,object>
	// }}

	public void RefMethods()
	{
		// object System.Text.Json.JsonSerializer.Deserialize<object>(string,System.Text.Json.Serialization.Metadata.JsonTypeInfo<object>)
		// System.Text.Json.Serialization.Metadata.JsonTypeInfo<object> System.Text.Json.JsonSerializer.GetTypeInfo<object>(System.Text.Json.JsonSerializerOptions)
		// object System.Text.Json.JsonSerializer.ReadFromSpan<object>(System.ReadOnlySpan<System.Char>,System.Text.Json.Serialization.Metadata.JsonTypeInfo<object>)
		// object System.Text.Json.JsonSerializer.ReadFromSpan<object>(System.ReadOnlySpan<byte>,System.Text.Json.Serialization.Metadata.JsonTypeInfo<object>,System.Nullable<int>)
		// string System.Text.Json.JsonSerializer.Serialize<object>(object,System.Text.Json.JsonSerializerOptions)
		// string System.Text.Json.JsonSerializer.Serialize<object>(object,System.Text.Json.Serialization.Metadata.JsonTypeInfo<object>)
		// string System.Text.Json.JsonSerializer.WriteString<object>(object&,System.Text.Json.Serialization.Metadata.JsonTypeInfo<object>)
		// System.Text.Json.Serialization.Metadata.JsonTypeInfo<int> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateCore<int>(System.Text.Json.Serialization.JsonConverter,System.Text.Json.JsonSerializerOptions)
		// System.Text.Json.Serialization.Metadata.JsonTypeInfo<object> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateCore<object>(System.Text.Json.JsonSerializerOptions,System.Text.Json.Serialization.Metadata.JsonCollectionInfoValues<object>,System.Text.Json.Serialization.JsonConverter<object>,object,object)
		// System.Text.Json.Serialization.Metadata.JsonTypeInfo<object> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateCore<object>(System.Text.Json.JsonSerializerOptions,System.Text.Json.Serialization.Metadata.JsonObjectInfoValues<object>)
		// System.Text.Json.Serialization.Metadata.JsonTypeInfo<object> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateCore<object>(System.Text.Json.Serialization.JsonConverter,System.Text.Json.JsonSerializerOptions)
		// System.Text.Json.Serialization.Metadata.JsonTypeInfo<object> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateListInfo<object,object>(System.Text.Json.JsonSerializerOptions,System.Text.Json.Serialization.Metadata.JsonCollectionInfoValues<object>)
		// System.Text.Json.Serialization.Metadata.JsonTypeInfo<object> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateObjectInfo<object>(System.Text.Json.JsonSerializerOptions,System.Text.Json.Serialization.Metadata.JsonObjectInfoValues<object>)
		// System.Text.Json.Serialization.Metadata.JsonPropertyInfo System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreatePropertyInfo<int>(System.Text.Json.JsonSerializerOptions,System.Text.Json.Serialization.Metadata.JsonPropertyInfoValues<int>)
		// System.Text.Json.Serialization.Metadata.JsonPropertyInfo System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreatePropertyInfo<object>(System.Text.Json.JsonSerializerOptions,System.Text.Json.Serialization.Metadata.JsonPropertyInfoValues<object>)
		// System.Text.Json.Serialization.Metadata.JsonPropertyInfo<int> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreatePropertyInfoCore<int>(System.Text.Json.Serialization.Metadata.JsonPropertyInfoValues<int>,System.Text.Json.JsonSerializerOptions)
		// System.Text.Json.Serialization.Metadata.JsonPropertyInfo<object> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreatePropertyInfoCore<object>(System.Text.Json.Serialization.Metadata.JsonPropertyInfoValues<object>,System.Text.Json.JsonSerializerOptions)
		// System.Text.Json.Serialization.Metadata.JsonTypeInfo<int> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<int>(System.Text.Json.JsonSerializerOptions,System.Text.Json.Serialization.JsonConverter)
		// System.Text.Json.Serialization.Metadata.JsonTypeInfo<object> System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<object>(System.Text.Json.JsonSerializerOptions,System.Text.Json.Serialization.JsonConverter)
		// System.Text.Json.Serialization.JsonConverter<object> System.Text.Json.Serialization.Metadata.JsonMetadataServices.GetConverter<object>(System.Text.Json.Serialization.Metadata.JsonObjectInfoValues<object>)
	}
}