using System.Reflection;

namespace Omamagoto.Tests
{
    /// <summary>
    /// テストで、コンポーネントの [SerializeField] の欄（private）に値を入れる道具。
    /// シーンでは Inspector やキッチンを作るスクリプトが入れている欄。
    /// </summary>
    public static class TestFields
    {
        public static void Set(object component, string fieldName, object value)
        {
            var field = component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new System.ArgumentException($"{component.GetType().Name} に {fieldName} という欄がない");
            field.SetValue(component, value);
        }
    }
}
