using System;

namespace Ruya.Core
{
    public static class ArrayHelper
    {
        // COMMENT method ToObjectArray
        // TEST method ToObjectArray
        public static object[] ToObjectArray(this Array input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            var objectArray = new object[input.Length];
            input.CopyTo(objectArray, 0);
            return objectArray;
        }
    }
}