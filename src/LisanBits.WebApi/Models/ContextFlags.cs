namespace LisanBits.WebApi.Models;

[Flags]
public enum ContextFlags : uint
{
    None = 0,
    General = 1 << 0,
    Religion = 1 << 1,
    Science = 1 << 2,
    Medical = 1 << 3,
    Astronomy = 1 << 4,
    Slang = 1 << 5,
    History = 1 << 6,
    Language = 1 << 7
}
