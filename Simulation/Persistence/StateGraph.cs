using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Persistence;

/// <summary>
/// Instantané des données de simulation, y compris leurs références partagées et leurs cycles.
/// Seuls les types de données explicitement autorisés sont restaurés ; aucun nom CLR externe ni délégué n'est chargé.
/// Les champs privés évitent de perdre les réservations, chemins, délais et compteurs des travaux en cours.
/// Le format est lié à son schéma : une modification de ces champs exige une migration ou un nouveau format.
/// </summary>
internal static class StateGraph
{
    private static readonly Type[] DataTypes =
    [
        typeof(WorldState), typeof(WorldMap), typeof(LocalMap), typeof(GameClock), typeof(Colony), typeof(Colonist),
        typeof(Needs), typeof(Skills), typeof(Personality), typeof(Stockpile), typeof(LaborLedger), typeof(Building),
        typeof(Field), typeof(FieldPlot), typeof(Canal), typeof(Activity), typeof(Grave), typeof(Thought),
        typeof(ColonySensors), typeof(ChainDemand), typeof(BreadDemand), typeof(Caravan), typeof(TradeLine),
        typeof(TradeRecord), typeof(Prayer), typeof(PrayerBook), typeof(ColonyBrain.NarrationTopic),
        typeof(WorldGrid), typeof(WorldTile), typeof(WorldRoute),
    ];
    private static readonly Dictionary<string, Type> KnownTypes = DataTypes
        .Concat(typeof(WorldState).Assembly.GetTypes().Where(t => t.IsEnum))
        .Concat(new[] { typeof(int), typeof(long), typeof(uint), typeof(byte), typeof(bool), typeof(float), typeof(double), typeof(string) })
        .ToDictionary(t => t.FullName!, StringComparer.Ordinal);
    private static readonly Dictionary<Type, FieldInfo[]> FieldCache = DataTypes.ToDictionary(t => t, Fields);
    private const int MaxItems = 262144;
    private const int MaxObjects = 250000;
    private const int MaxDepth = 512;

    internal static string Schema => string.Join("\n", DataTypes.OrderBy(t => t.FullName).Select(t =>
        t.FullName + ":" + string.Join(",", Fields(t).Select(f => f.Name + "=" + f.FieldType)))) + "\n" + RandomState.Schema;

    private static FieldInfo[] Fields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(f => !typeof(Delegate).IsAssignableFrom(f.FieldType)
            && !(type == typeof(LocalMap) && f.Name == "_scratch")
            && f.FieldType != typeof(Pathfinding.Pathfinder))
        .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();

    internal static void Write(BinaryWriter output, WorldState world) => new Writer(output).Value(world, 0);
    internal static WorldState Read(BinaryReader input)
    {
        var world = new Reader(input).Value(0) as WorldState ?? throw new InvalidDataException("Le monde est absent de la sauvegarde.");
        Validate(world);
        foreach (Colony colony in world.Colonies)
        {
            colony.Pathfinder = new Pathfinding.Pathfinder(colony.Map);
            if (FieldCache[typeof(Colony)].Single(f => f.Name == "_prayers").GetValue(colony) is PrayerBook book)
                foreach (Prayer prayer in book.All) prayer.RestoreAction();
        }
        return world;
    }

    private static void Validate(WorldState world)
    {
        if (world.Colonies.Count > WorldState.MaxPlayerColonies || world.Clock.Ticks < 0)
            throw new InvalidDataException("Le monde sauvegardé est invalide.");
        LocalMap[] maps = world.Colonies.Count == 0 ? [world.Map] : world.Colonies.Select(c => c.Map).ToArray();
        foreach (LocalMap map in maps)
        {
            if (map.Width is < 32 or > 512 || map.Height is < 32 or > 512)
                throw new InvalidDataException("Dimensions du terrain invalides.");
            foreach (FieldInfo field in Fields(typeof(LocalMap)))
                if (field.GetValue(map) is Array array && array.Length != map.Width * map.Height)
                    throw new InvalidDataException("Données du terrain incomplètes.");
        }
        foreach (Colony colony in world.Colonies)
        {
            if (colony.GatherSpots.Count == 0 || !colony.Map.InBounds(colony.CampX, colony.CampY) || colony.Clock != world.Clock)
                throw new InvalidDataException("Données de colonie invalides.");
            _ = world.WorldMap.PositionOf(colony);
        }
    }

    // Les identifiants de types sont structurels pour les collections ; le reste passe par la liste fermée ci-dessus.
    private static void WriteType(BinaryWriter output, Type type)
    {
        if (KnownTypes.ContainsKey(type.FullName ?? "")) { output.Write((byte)0); output.Write(type.FullName!); return; }
        if (type.IsArray && type.GetArrayRank() == 1) { output.Write((byte)1); WriteType(output, type.GetElementType()!); return; }
        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            byte kind = definition == typeof(List<>) ? (byte)2 : definition == typeof(Dictionary<,>) ? (byte)3
                : definition == typeof(HashSet<>) ? (byte)4 : definition == typeof(ValueTuple<,>) ? (byte)5
                : throw new InvalidDataException($"Collection non prise en charge : {type.Name}.");
            output.Write(kind);
            foreach (Type arg in type.GetGenericArguments()) WriteType(output, arg);
            return;
        }
        throw new InvalidDataException($"Type non pris en charge : {type.Name}.");
    }

    private static Type ReadType(BinaryReader input, int depth = 0)
    {
        if (depth > 12) throw new InvalidDataException("Type de sauvegarde trop complexe.");
        byte kind = input.ReadByte();
        if (kind == 0)
        {
            string name = input.ReadString();
            return KnownTypes.TryGetValue(name, out Type? type) ? type : throw new InvalidDataException("Type de sauvegarde inconnu.");
        }
        Type first = ReadType(input, depth + 1);
        return kind switch
        {
            1 => first.MakeArrayType(),
            2 => typeof(List<>).MakeGenericType(first),
            3 => typeof(Dictionary<,>).MakeGenericType(first, ReadType(input, depth + 1)),
            4 => typeof(HashSet<>).MakeGenericType(first),
            5 => typeof(ValueTuple<,>).MakeGenericType(first, ReadType(input, depth + 1)),
            _ => throw new InvalidDataException("Type de collection inconnu."),
        };
    }

    private sealed class Writer(BinaryWriter output)
    {
        private readonly Dictionary<object, int> _objects = new(ReferenceEqualityComparer.Instance);
        internal void Value(object? value, int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException("Le monde comporte trop de références imbriquées.");
            if (value is null) { output.Write((byte)0); return; }
            if (value is Species species) { output.Write((byte)2); output.Write(Species.All.ToList().IndexOf(species)); return; }
            Type type = value.GetType();
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum)
            {
                output.Write((byte)3); WriteType(output, type); Scalar(value, type); return;
            }
            if (!type.IsValueType)
            {
                if (_objects.TryGetValue(value, out int id)) { output.Write((byte)1); output.Write(id); return; }
                if (_objects.Count >= MaxObjects) throw new InvalidDataException("Le monde est trop volumineux.");
                _objects[value] = _objects.Count;
            }
            if (value is Random random)
            {
                output.Write((byte)4); RandomState.Write(output, random); return;
            }
            output.Write((byte)5); WriteType(output, type);
            if (value is Array array)
            {
                output.Write(array.Length);
                Type element = type.GetElementType()!;
                if (element.IsPrimitive)
                {
                    byte[] bytes = new byte[Buffer.ByteLength(array)];
                    Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length); output.Write(bytes);
                }
                else if (element.IsEnum)
                    foreach (object item in array) Scalar(item, element);
                else foreach (object? item in array) Value(item, depth + 1);
            }
            else if (value is IDictionary dictionary)
            {
                output.Write(dictionary.Count);
                foreach (DictionaryEntry entry in dictionary) { Value(entry.Key, depth + 1); Value(entry.Value, depth + 1); }
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
                && (definition == typeof(List<>) || definition == typeof(HashSet<>)))
            {
                var items = ((IEnumerable)value).Cast<object?>().ToArray();
                output.Write(items.Length); foreach (object? item in items) Value(item, depth + 1);
            }
            else
            {
                FieldInfo[] fields = FieldCache.GetValueOrDefault(type) ?? Fields(type);
                output.Write(fields.Length);
                foreach (FieldInfo field in fields) Value(field.GetValue(value), depth + 1);
            }
        }

        private void Scalar(object value, Type type)
        {
            if (type.IsEnum) { output.Write(Convert.ToInt64(value)); return; }
            switch (value)
            {
                case string text: output.Write(text); break;
                case int number: output.Write(number); break;
                case long number: output.Write(number); break;
                case uint number: output.Write(number); break;
                case byte number: output.Write(number); break;
                case bool flag: output.Write(flag); break;
                case float number: output.Write(number); break;
                case double number: output.Write(number); break;
                default: throw new InvalidDataException("Valeur non prise en charge.");
            }
        }
    }

    private sealed class Reader(BinaryReader input)
    {
        private readonly List<object> _objects = [];
        private int Count()
        {
            int count = input.ReadInt32();
            return count is >= 0 and <= MaxItems ? count : throw new InvalidDataException("Taille de collection invalide.");
        }
        private void Register(object value)
        {
            if (_objects.Count >= MaxObjects) throw new InvalidDataException("Trop d'objets dans la sauvegarde.");
            _objects.Add(value);
        }
        internal object? Value(int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException("Sauvegarde trop imbriquée.");
            byte tag = input.ReadByte();
            if (tag == 0) return null;
            if (tag == 1)
            {
                int id = input.ReadInt32();
                return id >= 0 && id < _objects.Count ? _objects[id] : throw new InvalidDataException("Référence de sauvegarde invalide.");
            }
            if (tag == 2)
            {
                int index = input.ReadInt32();
                return index >= 0 && index < Species.All.Count ? Species.All[index] : throw new InvalidDataException("Peuple inconnu.");
            }
            if (tag == 3) return Scalar(ReadType(input));
            if (tag == 4) { Random random = RandomState.Read(input); Register(random); return random; }
            if (tag != 5) throw new InvalidDataException("Élément de sauvegarde inconnu.");
            Type type = ReadType(input);
            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                throw new InvalidDataException("Type d'objet de sauvegarde invalide.");
            if (type.IsArray)
            {
                int count = Count(); Type element = type.GetElementType()!;
                Array array = Array.CreateInstance(element, count); Register(array);
                if (element.IsPrimitive)
                {
                    int size = Buffer.ByteLength(array); byte[] bytes = input.ReadBytes(size);
                    if (bytes.Length != size) throw new EndOfStreamException();
                    Buffer.BlockCopy(bytes, 0, array, 0, size);
                }
                else for (int i = 0; i < count; i++) array.SetValue(element.IsEnum ? Scalar(element) : Value(depth + 1), i);
                return array;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
                && (definition == typeof(List<>) || definition == typeof(Dictionary<,>) || definition == typeof(HashSet<>)))
            {
                object collection = Activator.CreateInstance(type)!; Register(collection);
                int count = Count();
                if (collection is IDictionary dictionary)
                    for (int i = 0; i < count; i++) dictionary.Add(Value(depth + 1)!, Value(depth + 1));
                else
                {
                    MethodInfo add = type.GetMethod("Add")!;
                    for (int i = 0; i < count; i++) add.Invoke(collection, [Value(depth + 1)]);
                }
                return collection;
            }
            object instance = RuntimeHelpers.GetUninitializedObject(type);
            if (!type.IsValueType) Register(instance);
            FieldInfo[] fields = FieldCache.GetValueOrDefault(type) ?? Fields(type);
            if (Count() != fields.Length) throw new InvalidDataException("Le schéma de la sauvegarde ne correspond pas à cette version du jeu.");
            foreach (FieldInfo field in fields) field.SetValue(instance, Value(depth + 1));
            return instance;
        }
        private object Scalar(Type type)
        {
            if (type.IsEnum) return Enum.ToObject(type, input.ReadInt64());
            if (type == typeof(string)) return input.ReadString();
            if (type == typeof(int)) return input.ReadInt32();
            if (type == typeof(long)) return input.ReadInt64();
            if (type == typeof(uint)) return input.ReadUInt32();
            if (type == typeof(byte)) return input.ReadByte();
            if (type == typeof(bool)) return input.ReadBoolean();
            if (type == typeof(float)) return input.ReadSingle();
            if (type == typeof(double)) return input.ReadDouble();
            throw new InvalidDataException("Valeur de sauvegarde invalide.");
        }
    }

    /// <summary>
    /// État du générateur à graine de .NET 8. Le schéma vérifie explicitement l'implémentation avant toute restauration.
    /// Cela garde les mêmes tirages après chargement, sans rejouer toute la partie ni changer les graines existantes.
    /// </summary>
    private static class RandomState
    {
        private static readonly FieldInfo Impl = typeof(Random).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly Type Implementation = new Random(1).GetType() == typeof(Random)
            ? Impl.GetValue(new Random(1))!.GetType() : throw new NotSupportedException();
        private static readonly FieldInfo Prng = Implementation.GetField("_prng", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo[] Parts = Prng.FieldType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).OrderBy(f => f.Name).ToArray();
        internal static string Schema => Implementation.FullName + ":" + string.Join(",", Parts.Select(f => f.Name + "=" + f.FieldType));
        internal static void Write(BinaryWriter output, Random random)
        {
            object impl = Impl.GetValue(random)!;
            if (impl.GetType() != Implementation || Parts.Length != 3) throw new InvalidDataException("Générateur aléatoire incompatible.");
            object state = Prng.GetValue(impl)!;
            foreach (FieldInfo field in Parts)
                if (field.GetValue(state) is int[] array)
                {
                    output.Write(array.Length); foreach (int value in array) output.Write(value);
                }
                else output.Write((int)field.GetValue(state)!);
        }
        internal static Random Read(BinaryReader input)
        {
            var random = new Random(1); object impl = Impl.GetValue(random)!; object state = Prng.GetValue(impl)!;
            foreach (FieldInfo field in Parts)
                if (field.FieldType == typeof(int[]))
                {
                    if (input.ReadInt32() != 56) throw new InvalidDataException("État aléatoire invalide.");
                    var array = new int[56]; for (int i = 0; i < array.Length; i++) array[i] = input.ReadInt32();
                    field.SetValue(state, array);
                }
                else
                {
                    int value = input.ReadInt32();
                    if (value is < 0 or > 55) throw new InvalidDataException("État aléatoire invalide.");
                    field.SetValue(state, value);
                }
            Prng.SetValue(impl, state); return random;
        }
    }
}
