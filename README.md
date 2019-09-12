# Initial Setup
1. Add tags below into TASK LIST **Tools\Options\Environment\Task List**
    - HARD-CODED _priority high_ 
        - use to mark constants

        > `// HARD-CODED constant`
    - TRACE _priority high_ 
        - use to point trace must be implemented

        > `// TRACE method MethodName`
    - COMMENT _priority low_ 
        - use to point comment must be completed 

        > `// COMMENT property PropertyName`
    - TEST _priority low_ 
        - use to point unit test must be implemented 

        > `// TEST class ClassName`
2. Use warning tags to point ReFactor needed (including none complete codes) as `#warning Refactor`
3. Suppress CODE ANALYTICS in source code 
    - if you are 100% sure that, it will never need modification again
    - if you placed #warning Refactor
4. priority order is _CODE > ANALYTICS > TRACE > TEST > COMMENT_
