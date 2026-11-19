import test from 'node:test';
import assert from 'node:assert/strict';
import {createCalculatorLock,saveCalculatorLock,matchesCalculatorLock} from '../calculator-lock.js';

test('cold start and manual lock conceal chat; switching apps preserves the unlocked session', async()=>{
  class Element extends EventTarget {
    constructor(){super();this.dataset={};this.textContent='';this.firstElementChild={textContent:''};this.style={};this.clientWidth=0;this.classes=new Set();this.classList={add:x=>this.classes.add(x),remove:x=>this.classes.delete(x)};}
    close(){this.open=false;}
    setAttribute(name,value){this[name]=value;}
  }
  const ids=['#calculator','#calculator-display','#calculator-expression','#app','#toast','#sheet','meta[name="theme-color"]','#calculator-history','#calculator-screen','.calculator-keys','#calculator-history-button','#calculator-history-empty','#calculator-history-list','#calculator-basic-button'];
  const elements=Object.fromEntries(ids.map(id=>[id,new Element()]));
  let paused=0,badges=0,blurred=0;
  globalThis.document=Object.assign(new EventTarget(),{documentElement:new Element(),hidden:false,activeElement:{blur(){blurred++;}},querySelector:id=>elements[id],querySelectorAll:()=>[{pause(){paused++;}}]});
  globalThis.window=new EventTarget();
  const data=new Map();globalThis.localStorage={getItem:k=>data.get(k)||null,setItem:(k,v)=>data.set(k,v)};
  Object.defineProperty(globalThis,'navigator',{value:{clearAppBadge:async()=>{badges++;}},configurable:true});
  const {isCalculator,showCalculator,showNotice,initializeCalculator,calculatorSetupComplete}=await import('../camouflage.js');
  const type=text=>{for(const key of text){const event=new Event('keydown');Object.defineProperty(event,'key',{value:key});document.dispatchEvent(event);}};
  const reveal=async()=>{type('654321*87=');for(let i=0;isCalculator()&&i<200;i++)await new Promise(r=>setTimeout(r,10));assert.equal(isCalculator(),false);};
  const assertCovered=()=>{assert.equal(isCalculator(),true);assert.equal(document.title,'Máy tính');assert.equal(elements['#app'].hidden,true);assert.equal(elements['#app'].inert,true);assert.equal(elements['#toast'].classes.has('visible'),false);assert.equal(elements['#toast'].textContent,'');};
  assertCovered();showNotice('Tin bí mật vừa đến');assertCovered();
  initializeCalculator();assert.equal(isCalculator(),false); // First use enters the ordinary vault password gate.
  await saveCalculatorLock(await createCalculatorLock('654321*87'));calculatorSetupComplete();showCalculator();
  await reveal();assert.equal(isCalculator(),false);assert.equal(elements['#app'].hidden,false);assert.equal(elements['#app'].inert,false);
  assert.equal(elements['#calculator-history-list'].innerHTML,''); // Never record the access expression.
  showNotice('Tin mới khi đang chat');assert.equal(elements['#toast'].classes.has('visible'),true);
  elements['#sheet'].open=true;showCalculator();assertCovered();assert.equal(elements['#sheet'].open,false);assert.ok(paused>=2&&badges>=2&&blurred>=2);
  showNotice('Thông báo cập nhật đến muộn');await reveal();assert.equal(elements['#toast'].textContent,'');
  document.hidden=true;document.dispatchEvent(new Event('visibilitychange'));assert.equal(isCalculator(),false);
  document.hidden=false;document.dispatchEvent(new Event('visibilitychange'));assert.equal(isCalculator(),false);
  window.dispatchEvent(new Event('pagehide'));assert.equal(isCalculator(),false);
  const restored=new Event('pageshow');Object.defineProperty(restored,'persisted',{value:true});window.dispatchEvent(restored);assert.equal(isCalculator(),false);
  showCalculator();assertCovered();
  type('654321*87=');showCalculator();await matchesCalculatorLock('654321*87');assertCovered(); // A late check cannot uncover a backgrounded app.
  type('123456789*91=');await matchesCalculatorLock('654321*87');assertCovered();
  showCalculator();type('654321*87=');type('1');await matchesCalculatorLock('654321*87');assertCovered(); // Editing invalidates a pending check.
  showCalculator();type('2+3=+4=');
  assert.equal(elements['#calculator-display'].firstElementChild.textContent,'9'); // Arithmetic never waits for password hashing.
  await matchesCalculatorLock('654321*87');assertCovered();
});
