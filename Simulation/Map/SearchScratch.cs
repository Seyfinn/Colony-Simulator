namespace GodColony.Simulation.Map;

/// <summary>
/// Des tableaux de travail à la taille d'une carte, gardés d'une recherche à l'autre (tracé d'un canal, lac d'un barrage) :
/// les recréer à chaque fois, des dizaines de fois par heure de jeu, coûterait cher. Une case n'y compte que si elle porte
/// la marque de la recherche en cours, il n'y a donc rien à effacer entre deux recherches. Une seule recherche à la fois.
/// </summary>
internal sealed class SearchScratch(int size)
{
    private readonly int[] _visited = new int[size];
    private readonly int[] _blocked = new int[size];
    private int _visit, _block;

    /// <summary>Coût et case précédente des cases visitées par la recherche en cours.</summary>
    public float[] Cost { get; } = new float[size];
    public int[] Parent { get; } = new int[size];

    public PriorityQueue<int, float> Open { get; } = new();

    /// <summary>Les cases en attente (file d'un parcours en largeur) et celles retenues.</summary>
    public List<int> Queue { get; } = [];
    public List<int> Found { get; } = [];

    /// <summary>Commence une recherche : plus aucune case n'est visitée.</summary>
    public void NewSearch()
    {
        _visit = Next(_visit, _visited);
        Open.Clear();
        Queue.Clear();
        Found.Clear();
    }

    public bool IsVisited(int index) => _visited[index] == _visit;
    public void Visit(int index) => _visited[index] = _visit;

    /// <summary>Oublie les obstacles marqués jusqu'ici.</summary>
    public void NewObstacles() => _block = Next(_block, _blocked);

    public bool IsBlocked(int index) => _blocked[index] == _block;
    public void Block(int index) => _blocked[index] = _block;

    private static int Next(int mark, int[] marks)
    {
        if (mark < int.MaxValue)
            return mark + 1;
        Array.Clear(marks);
        return 1;
    }
}
