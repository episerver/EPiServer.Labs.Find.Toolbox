# EPiServer.Labs.Find.ImprovedSynonyms

ImprovedSynonyms offers a way around limitations of the Find synonym implementation.
Currently the synonym expansion is done in backend (Elastic Search) and relies on a synonym index.

ImprovedSynonyms solves limitations in the following scenarios:
* Missing or unexplainable hits when using .WithAndAsDefaultOperator()
* Multiple term synonyms
* Multiple term synonyms within quotes
* Multiple term synonyms requires all terms to match
* Does not rely on an synonym index to be up to date

This is solved by downloading and caching the synonym list, and when the query comes in
we expand the matching synonyms on the query, on the client side.

Searching for 'episerver find' where find is a synonym for 'search & navigation"
will result in 'episerver (find OR (search & navigation))'

When using ImprovedSynonyms with .WithAndAsDefaultOperator()
Elastic Search's MinimumShouldMatch is used which will emulate an AND operator with a twist.
Up to 2 terms all terms are required to match. >2 terms 60% of the terms are required to match.

[![License](http://img.shields.io/:license-apache-blue.svg?style=flat-square)](http://www.apache.org/licenses/LICENSE-2.0.html)

---

## Table of Contents

- [System requirements](#system-requirements)
- [Installation](#installation)

---

## System requirements

* Find 12 or higher

See also the general [Episerver system requirements](https://world.episerver.com/documentation/system-requirements/) on Episerver World.

---

## Installation

1. Copy all files into your project
2. Remove any use of .UsingSynonyms()
3. Add .UsingSynonymsImproved() to your Find search
4. We recommend using .WithAndAsDefaultOperator() but OR (default) will also work.