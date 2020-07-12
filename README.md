# EPiServer.Labs.Find.ImprovedSynonyms

ImprovedSynonyms offers a way around current limitations of the Find synonym implementation.
Currently the synonym expansion is done in backend (Elastic Search) and relies on a synonym index.

ImprovedSynonyms solves limitations in the following scenarios:
* Missing or unexplainable hits when using .WithAndAsDefaultOperator()
* Multiple term synonyms
* Multiple term synonyms bidirectional
* Multiple term synonyms within quotes
* Multiple term synonyms requires all terms to match
* Does not rely on an synonym index to be up to date
* No unwanted built-in synonyms

This is solved by retrieving and caching the synonym list, and when the query comes in
we expand the matching synonyms on the query, on the client side.

Searching for 'episerver find' where find is a synonym for 'search & navigation"
will result in 'episerver (find OR (search & navigation))'

ImprovedSynonyms also comes with support for Elastic Search's MinimumShouldMatch. 
With MinimumShouldMatch it's possible to set or or more conditions for how many terms (in percentage and absolutes) should match.
If you specify 2<60% all terms up to 2 terms will be required to match. More than 2 terms 60% of the terms are required to match.

Note!
* There will always be an OR relationship between the synonym match and the expanded synonym regardless if you use WithAndAsDefaultOperator() or MinimumShouldMatch().
* There will always be an AND relationship between terms of the phrase in the synonym match and the expanded synonym regardless if you use OR.

[MinimumShouldMatch documentation](https://www.elastic.co/guide/en/elasticsearch/reference/current/query-dsl-minimum-should-match.html)

[![License](http://img.shields.io/:license-apache-blue.svg?style=flat-square)](http://www.apache.org/licenses/LICENSE-2.0.html)

---

## Table of Contents

- [System requirements](#system-requirements)
- [Installation](#installation)

---

## System requirements

* Find 12 or higher
* .NET Framework 4.6.1 or higher

See also the general [Episerver system requirements](https://world.episerver.com/documentation/system-requirements/) on Episerver World.

---

## Installation

1. Copy all files into your project or install NuGet package

2. Make sure you use 

   ```csharp
   using EPiServer.Find.Cms;
   ``` 
3. Remove any use of .UsingSynonyms()

4. Add .WithAndAsDefaultOperator if you want but we recommend .MinimumShouldMatch(). Not specifying either will allow for OR as the default operator.
   Using MinimumShouldMatch() will preced any use of .WithAndAsDefaultOperator() or the default OR.

5. Add .UsingSynonymsImproved([cacheDuration])
   The cache duration parameter defaults to 1 hour but could be set to something shorter during testing.

6. It could look like this

    ```csharp
    // With MinimumShouldMatch() with conditions
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)             
                                    .MinimumShouldMatch("2<60%")
                                    .UsingSynonymsImproved()                                         
                                    .GetResult();
    ```
    
    ```csharp
    // With MinimumShouldMatch() absolutes    
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)             
                                    .MinimumShouldMatch("2")
                                    .UsingSynonymsImproved()                                         
                                    .GetResult();
    ```
    
    ```csharp
    // With WithAndAsDefaultOperator() 
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)             
                                    .WithAndAsDefaultOperator()
                                    .UsingSynonymsImproved()                                         
                                    .GetResult();
    ```

    ```csharp
    // Without WithAndAsDefaultOperator() which is the default behaviour which sets the default operator to OR
    UnifiedSearchResults results = SearchClient.Instance.UnifiedSearch(Language.English)
                                    .For(query)                 
                                    .UsingSynonymsImproved()                                         
                                    .GetResult();
    ```

7. Enjoy!

