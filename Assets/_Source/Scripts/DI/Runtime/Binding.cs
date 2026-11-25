using System;

namespace RaidIntoDarkness.DI
{
    public sealed class Binding
    {
        public Type Abstraction { get; set; }
        public Func<IContainer, object> Factory { get; set; }
        public Lifetime Lifetime { get; set; }
        public object SingletonInstance { get; set; }
        public bool IsExternInstance { get; set; }
    }
}
