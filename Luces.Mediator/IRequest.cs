namespace Luces.Mediator;

public interface IRequest<out TResponse>
{

}

public interface IStreamRequest<out TResponse>
{

}

public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
