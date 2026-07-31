namespace AcKrovy.Cad.Abstractions.Drawing;

public interface IDrawingAnnotationScaleStore
{
    bool TryRead(out int denominator);

    void Write(int denominator);

    void Remove();
}
