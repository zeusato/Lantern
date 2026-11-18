import test from 'node:test';
import assert from 'node:assert/strict';
import {hasCalculatorLock,createCalculatorLock,saveCalculatorLock,matchesCalculatorLock,validateCalculatorExpression} from '../calculator-lock.js';

test('personal expressions persist only salted hashes; replacement removes old access', async()=>{
  const data = new Map();
  globalThis.localStorage = {getItem:k=>data.get(k)??null,setItem:(k,v)=>data.set(k,v)};
  assert.equal(hasCalculatorLock(),false);
  assert.equal(await matchesCalculatorLock('123456789*91'),false);
  const first=await createCalculatorLock('654321 × 87');
  const second=await createCalculatorLock('654321*87');
  assert.notEqual(first.salt,second.salt);assert.notEqual(first.hash,second.hash);
  saveCalculatorLock(first);assert.equal(hasCalculatorLock(),true);
  assert.equal([...data.values()].join().includes('654321'),false);
  assert.equal(await matchesCalculatorLock('654321*87'),true);
  assert.equal(await matchesCalculatorLock('123456789*91'),false);
  assert.equal(await matchesCalculatorLock('56925927'),false); // Same result is not the expression.
  const replacement=await createCalculatorLock('123456+78');
  const oldCheck=matchesCalculatorLock('654321*87');
  saveCalculatorLock(replacement);
  assert.equal(await oldCheck,false); // A concurrent settings change invalidates an in-flight check.
  assert.equal(await matchesCalculatorLock('654321*87'),false);
  assert.equal(await matchesCalculatorLock('123456+78'),true);
  data.set([...data.keys()][0],'{broken');
  assert.equal(hasCalculatorLock(),false);
  assert.equal(await matchesCalculatorLock('123456789*91'),false);
  localStorage.setItem=()=>{throw Error('quota');};
  assert.throws(()=>saveCalculatorLock(first),/quota/);
});

test('configuration supports mobile notation and rejects expressions impossible to enter',()=>{
  for(const [input,expected] of [['123456 × 91 =','123456*91'],['123,456 ÷ 7','123.456/7'],['-123456+7','-123456+7']])
    assert.equal(validateCalculatorExpression(input),expected);
  for(const value of ['12+34','123456','123456/0','123456++7','(123456+7)','123456+','0012345+.6','1e6+1','9'.repeat(80)+'+1','<script>'])
    assert.throws(()=>validateCalculatorExpression(value),value);
});
