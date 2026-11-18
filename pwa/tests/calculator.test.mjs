import test from 'node:test';
import assert from 'node:assert/strict';
import {calculate,Calculator} from '../calculator.js';
test('calculator evaluates arithmetic, precedence, decimals, signs and percentages without eval',()=>{
 for(const [expression,result] of [['2+3*4',14],['(2+3)*4',20],['0.1+0.2',.3],['200+10%',220],['200-10%',180],['200*10%',20],['50%',.5],['-3*-2',6],['123456789*91',11234567799]])assert.equal(calculate(expression),result,expression);
 for(const invalid of ['1/0','window.location','alert(1)','1..2','(2+3','2**3','9'.repeat(200)])assert.throws(()=>calculate(invalid));
});
test('the former shared expression is now ordinary arithmetic with no built-in unlock',()=>{
 const calc=new Calculator();for(const key of '123456789*91')assert.equal(calc.press(key),false);
 assert.equal(calc.press('='),false);assert.equal(calc.display,'11234567799');
});
test('calculator keypad supports clear, backspace, sign change, continuation and recovery',()=>{
 const calc=new Calculator();for(const key of ['1','2','backspace','±','*','3','='])calc.press(key);assert.equal(calc.display,'−3');
 for(const key of ['+','5','='])calc.press(key);assert.equal(calc.display,'2');
 for(const key of ['9','/','0','='])calc.press(key);assert.equal(calc.display,'Lỗi');
 for(const key of ['.','5','+','.','5','='])calc.press(key);assert.equal(calc.display,'1');
 calc.press('AC');assert.equal(calc.display,'0');assert.equal(calc.history,'');
});
test('small and large results can continue calculating and change sign',()=>{
 const calc=new Calculator();for(const key of '1/100000000=')calc.press(key);
 for(const key of ['±','*','2','='])calc.press(key);assert.equal(calculate(calc.expression),-2e-8);
 calc.press('AC');for(const key of '999999999999*999999999999=')calc.press(key);
 for(const key of ['/','2','='])calc.press(key);assert.equal(calculate(calc.expression),4.99999999999e23);
});
