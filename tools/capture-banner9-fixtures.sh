#!/usr/bin/env bash
# Captures Banner 9 fixtures for one school into tests/fixtures/banner9/<school-id>/.
#
# usage: tools/capture-banner9-fixtures.sh <school-id> <base-url> <term-code> [mep-code]
#   tools/capture-banner9-fixtures.sh eku https://registrationss.eku.edu/StudentRegistrationSsb/ 202710
#   tools/capture-banner9-fixtures.sh uiuc https://banner.apps.uillinois.edu/StudentRegistrationSSB/ 120268 1UIUC
#
# Makes 6 requests, 1 second apart. Responses are saved verbatim; never hand-edit them.
set -euo pipefail

if [ $# -lt 3 ]; then
  sed -n '4,6p' "$0"
  exit 2
fi

school=$1
base=${2%/}/
term=$3
mep=${4:-}
mep_query=""
if [ -n "$mep" ]; then mep_query="&mepCode=$mep"; fi

repo=$(cd "$(dirname "$0")/.." && pwd)
out="$repo/tests/fixtures/banner9/$school"
mkdir -p "$out"
jar=$(mktemp)
trap 'rm -f "$jar"' EXIT

user_agent="DueGooder/0.1 (HACK KENTUCKY 2026 course-data bounty)"
session_id="dg$(date +%s)"

# fetch <file> <curl args...>: saves the body to <file>, prints and returns the HTTP status.
fetch() {
  local file=$1; shift
  local status
  status=$(curl -s -A "$user_agent" -c "$jar" -b "$jar" -o "$out/$file" -w "%{http_code}" "$@")
  echo "  $file -> HTTP $status, $(wc -c < "$out/$file" | tr -d ' ') bytes" >&2
  echo "$status"
}

echo "Capturing $school, term $term, into ${out#$repo/}" >&2

status=$(fetch getTerms.json "${base}ssb/classSearch/getTerms?searchTerm=&offset=1&max=100$mep_query")
if [ "$status" != 200 ]; then
  echo "FAILED: getTerms returned HTTP $status. Check base_url (and mep_code) in config/schools.yaml." >&2
  exit 1
fi
sleep 1

status=$(fetch term-search.json -X POST \
  --data "term=$term&studyPath=&studyPathText=&startDatepicker=&endDatepicker=&uniqueSessionId=$session_id" \
  "${base}ssb/term/search?mode=search$mep_query")
if [ "$status" != 200 ]; then
  rm -f "$out/term-search.json"
  echo "FAILED: term/search returned HTTP $status. A 302 usually means class search needs a login;" >&2
  echo "        we only collect public data, so pick another school and note this one in the doc." >&2
  exit 1
fi

for offset in 0 10; do
  sleep 1
  fetch "searchResults-$offset.json" \
    "${base}ssb/searchResults/searchResults?txt_term=$term&startDatepicker=&endDatepicker=&uniqueSessionId=$session_id&pageOffset=$offset&pageMaxSize=10&sortColumn=subjectDescription&sortDirection=asc$mep_query" > /dev/null
done
sleep 1

fetch resetDataForm.json -X POST --data "resetCourses=false&resetSections=true" \
  "${base}ssb/classSearch/resetDataForm?${mep_query#&}" > /dev/null

# A result page with no sections means the term or session was wrong, not that the fixture is fine.
if grep -q '"totalCount": *0[,}]' "$out/searchResults-0.json" || grep -q '"data": *null' "$out/searchResults-0.json"; then
  echo "FAILED: searchResults came back empty. Check the term code is one getTerms.json lists." >&2
  exit 1
fi
echo "OK: $school captured." >&2
