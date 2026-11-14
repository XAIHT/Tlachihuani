import assert from 'node:assert/strict';

export function answerMatches(name, answer, expected) {
  const trimmed=answer.trim();
  const fenced=/^```(?:[A-Za-z][\w+-]*)?[ \t]*\r?\n([\s\S]*?)\r?\n```$/.exec(trimmed);
  const clean=fenced?fenced[1].trim():trimmed.replace(/^```(?:json)?\s*\n?|\n?```$/g,'').trim();
  if(typeof expected==='object'){
    try{assert.deepEqual(JSON.parse(clean),expected);return true;}catch{return false;}
  }
  // This case checks comprehension and requests yes/no, without a casing rule.
  // Keep case-sensitive assertions for the explicit casing and literal cases.
  if(name==='negation comprehension')return clean.toLowerCase()===expected;
  return clean===expected||clean.split('\n').at(-1).trim()===expected;
}
