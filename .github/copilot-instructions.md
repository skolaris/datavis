# Response curbing
- Output only affected methods when suggesting fixes or improvements

# General code generation
- Start summary XML comments with a capitalised verb and end with a period.
- Start other comments with lowercase unless multiple sentences
- In comments omit space between slashes and text
- Place all private members at the bottom of the class
- Omit 'private' and 'inline' keywords
- Prefer var in declarations
- Prefer collection expressions

# Testing code generation
- Omit arrange comment, use act comment, use assert comment only if there are three or more assertions
- Keep tests with five or fewer lines of code without comments and blank lines
- Use FluentAssertions
- Use Method_Conditions_Result test naming scheme
- Use readonly members for mocks and the tested class
- Use _sut for the tested class member variable
- Initialize mocks inline and the tested class in the constructor unless it has zero parameters, in which case initialize it inline as well
- Use Verifiable() for mock setup
- Use VerifyNoOtherCalls for ensuring no other calls were made to the mock
- Place the happy path test last
